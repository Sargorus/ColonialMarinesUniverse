using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Shared.CMU14.Robotics.QueueNetwork;

/// <summary>
/// M/M/1 steady-state formulas and the flow-balance solver used by the
/// production diagnostic terminal.
///
/// The terminal deliberately uses the textbook formulas: when a node is
/// overloaded (rho &gt;= 1), L, Lq, W and Wq come out negative or infinite.
/// That is the same signal a student sees in a queueing-theory lab, and it is
/// how the terminal flags a missing steady state instead of simply painting
/// the node red.
/// </summary>
public static class CMUQueueNetworkMath
{
    private const double Epsilon = 1e-9;

    /// <summary>
    /// Solves the network flow-balance equations
    /// lambda_i = source_i + sum_j lambda_j * p(j -> i).
    /// Routes whose destination is not a node are treated as the sink.
    /// </summary>
    public static bool TrySolveArrivalRates(
        CMUQueueNetworkPrototype network,
        out Dictionary<string, double> rates,
        out string error)
    {
        rates = new Dictionary<string, double>(StringComparer.Ordinal);
        error = string.Empty;

        var ids = new List<string>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in network.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                error = "A queue network node has an empty id.";
                return false;
            }

            if (index.ContainsKey(node.Id))
            {
                error = $"Duplicate queue network node id '{node.Id}'.";
                return false;
            }

            index[node.Id] = ids.Count;
            ids.Add(node.Id);
        }

        if (ids.Count == 0)
        {
            error = "Queue network has no nodes.";
            return false;
        }

        var count = ids.Count;
        var matrix = new double[count, count + 1];

        for (var i = 0; i < count; i++)
            matrix[i, i] = 1;

        foreach (var source in network.Sources)
        {
            if (!index.TryGetValue(source.Node, out var nodeIndex))
            {
                error = $"Queue network source references unknown node '{source.Node}'.";
                return false;
            }

            matrix[nodeIndex, count] += source.Rate;
        }

        foreach (var route in network.Routes)
        {
            if (!index.TryGetValue(route.From, out var from))
            {
                error = $"Queue network route starts at unknown node '{route.From}'.";
                return false;
            }

            // Anything that does not point at a known node leaves the network.
            if (!index.TryGetValue(route.To, out var to))
                continue;

            matrix[to, from] -= route.Probability;
        }

        if (!SolveLinearSystem(matrix, count, out error))
            return false;

        for (var i = 0; i < count; i++)
        {
            var value = matrix[i, count];
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                error = "Queue network flow balance produced a non-finite arrival rate.";
                return false;
            }

            rates[ids[i]] = value;
        }

        return true;
    }

    /// <summary>Textbook M/M/1 metrics for one node.</summary>
    public static CMUQueueMetrics ComputeMetrics(double lambda, double mu)
    {
        if (mu <= 0)
        {
            return new CMUQueueMetrics(
                lambda,
                mu,
                double.PositiveInfinity,
                double.PositiveInfinity,
                double.PositiveInfinity,
                double.PositiveInfinity,
                double.PositiveInfinity,
                false);
        }

        var rho = lambda / mu;
        var stable = rho < 1 - Epsilon;
        var denominator = 1 - rho;

        // At rho == 1 the steady state does not exist and the formulas divide
        // by zero. Treat it as an infinite backlog instead of NaN.
        if (Math.Abs(denominator) < Epsilon)
        {
            return new CMUQueueMetrics(
                lambda,
                mu,
                rho,
                double.PositiveInfinity,
                double.PositiveInfinity,
                double.PositiveInfinity,
                double.PositiveInfinity,
                false);
        }

        var l = rho / denominator;
        var lq = rho * rho / denominator;
        var w = 1 / (mu - lambda);
        var wq = lambda / (mu * (mu - lambda));

        return new CMUQueueMetrics(lambda, mu, rho, l, lq, w, wq, stable);
    }

    /// <summary>
    /// Projects the offered-load backlog forward. Each node receives its
    /// flow-balance arrival rate; a node with lambda &gt; mu grows, a node with
    /// lambda &lt; mu drains any existing queue and then stays at zero.
    ///
    /// This is a deterministic fluid projection on purpose: it keeps the chart
    /// readable and makes the effect of an upgrade unambiguous.
    /// </summary>
    public static List<CMUQueueProjectionPoint> Project(
        CMUQueueNetworkPrototype network,
        IReadOnlyDictionary<string, double> serviceRates,
        IReadOnlyDictionary<string, double> arrivalRates,
        IReadOnlyDictionary<string, double> startQueues,
        int minutes,
        int stepsPerMinute = 4)
    {
        var queues = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var node in network.Nodes)
        {
            queues[node.Id] = startQueues.TryGetValue(node.Id, out var queue)
                ? Math.Max(0, queue)
                : 0;
        }

        var result = new List<CMUQueueProjectionPoint>
        {
            new(0, new Dictionary<string, double>(queues, StringComparer.Ordinal)),
        };

        if (minutes <= 0)
            return result;

        stepsPerMinute = Math.Max(1, stepsPerMinute);
        var dt = 1.0 / stepsPerMinute;

        for (var minute = 1; minute <= minutes; minute++)
        {
            for (var step = 0; step < stepsPerMinute; step++)
            {
                foreach (var node in network.Nodes)
                {
                    var lambda = arrivalRates.TryGetValue(node.Id, out var arrival) ? arrival : 0;
                    var mu = serviceRates.TryGetValue(node.Id, out var service) ? service : 0;
                    var queue = queues[node.Id];
                    queue += (lambda - mu) * dt;
                    queues[node.Id] = Math.Max(0, queue);
                }
            }

            result.Add(new CMUQueueProjectionPoint(
                minute,
                new Dictionary<string, double>(queues, StringComparer.Ordinal)));
        }

        return result;
    }

    private static bool SolveLinearSystem(double[,] matrix, int count, out string error)
    {
        error = string.Empty;

        for (var column = 0; column < count; column++)
        {
            var pivot = column;
            var best = Math.Abs(matrix[column, column]);
            for (var row = column + 1; row < count; row++)
            {
                var candidate = Math.Abs(matrix[row, column]);
                if (candidate > best)
                {
                    best = candidate;
                    pivot = row;
                }
            }

            if (best < Epsilon)
            {
                error = "Queue network has a closed loop with no exit; arrival rates are unbounded.";
                return false;
            }

            if (pivot != column)
            {
                for (var columnIndex = column; columnIndex <= count; columnIndex++)
                {
                    (matrix[column, columnIndex], matrix[pivot, columnIndex]) =
                        (matrix[pivot, columnIndex], matrix[column, columnIndex]);
                }
            }

            var divisor = matrix[column, column];
            for (var columnIndex = column; columnIndex <= count; columnIndex++)
                matrix[column, columnIndex] /= divisor;

            for (var row = 0; row < count; row++)
            {
                if (row == column)
                    continue;

                var factor = matrix[row, column];
                if (Math.Abs(factor) < Epsilon)
                    continue;

                for (var columnIndex = column; columnIndex <= count; columnIndex++)
                    matrix[row, columnIndex] -= factor * matrix[column, columnIndex];
            }
        }

        return true;
    }
}

public readonly record struct CMUQueueMetrics(
    double Lambda,
    double Mu,
    double Rho,
    double L,
    double Lq,
    double W,
    double Wq,
    bool Stable);

public sealed record CMUQueueProjectionPoint(
    int Minute,
    IReadOnlyDictionary<string, double> Queues);
