cmu-queue-console-title = Production Diagnostic Terminal
cmu-queue-console-heading = Production diagnostics
cmu-queue-console-subheading = M/M/1 queue network
cmu-queue-console-budget = Maintenance budget
cmu-queue-console-time = Diagnostic time
cmu-queue-console-time-value = { $left }/{ $total } min
cmu-queue-console-reward = Contract payout
cmu-queue-console-reference = Reference
cmu-queue-console-idle = No active contract
cmu-queue-console-idle-desc = Accept a diagnostic contract to inspect a colony production line. Each contract gives you a maintenance budget and a fifteen-minute diagnostic window. Analyze nodes, find the ones without a steady state, upgrade their service rates, and submit the line once every queue is stable.
cmu-queue-console-request = Request diagnostic contract
cmu-queue-console-overview = Contract
cmu-queue-console-line = Production line
cmu-queue-console-line-hint = Select a node to inspect it. Q is the projected backlog and μ is the service rate.
cmu-queue-console-node-tooltip = Click to inspect this node.
cmu-queue-console-service = Service rate μ
cmu-queue-console-external = External arrivals
cmu-queue-console-queue = Projected queue
cmu-queue-console-upgrades = Upgrades
cmu-queue-console-lambda = Arrival rate λ
cmu-queue-console-rho = Utilization ρ
cmu-queue-console-l = Average in system L
cmu-queue-console-lq = Average queue Lq
cmu-queue-console-w = Average time W
cmu-queue-console-wq = Average wait Wq
cmu-queue-console-no-data = No diagnostic data. Analyze this node to reveal λ, ρ and the steady-state values.
cmu-queue-console-analyze = Analyze node ({ $cost } min)
cmu-queue-console-stable-hint = Steady state exists: ρ < 1. The node keeps up with its offered load of { $lambda }.
cmu-queue-console-unstable-hint = NO STEADY STATE: ρ ≥ 1. The queue grows without bound, and the negative L/Lq values are the formulas' way of saying so. Increase μ above λ or reduce the incoming flow.
cmu-queue-console-upgrade = Upgrade μ to { $rate } ({ $cost })
cmu-queue-console-max-upgrades = Maximum upgrades installed
cmu-queue-console-routes = Routing
cmu-queue-console-routes-hint = Each node sends this fraction of its output onward. Everything not routed to a node leaves the network.
cmu-queue-console-sink = shipment
cmu-queue-console-projection = Queue projection
cmu-queue-console-project = Project { $minutes } min ({ $cost } min of diagnostics)
cmu-queue-console-actions = Contract actions
cmu-queue-console-submit = Submit line for inspection
cmu-queue-console-reset = Refund all upgrades
cmu-queue-console-new-contract = Request another contract
cmu-queue-console-completed = Contract complete
cmu-queue-console-completed-hint = Payout of { $reward } credits transferred to the colony budget.

cmu-queue-status-idle = No active contract.
cmu-queue-status-started = Contract accepted: { $network }.
cmu-queue-status-analyzed = { $node } analyzed: ρ = { $rho }.
cmu-queue-status-already-analyzed = { $node } is already analyzed.
cmu-queue-status-no-time = Not enough diagnostic time for that action.
cmu-queue-status-upgraded = { $node } service rate increased to { $rate }/min.
cmu-queue-status-max-upgrades = { $node } has no more upgrade slots.
cmu-queue-status-no-budget = Not enough maintenance budget.
cmu-queue-status-reset = Upgrades refunded: { $refund } credits returned to the maintenance budget.
cmu-queue-status-reset-none = No upgrades to refund.
cmu-queue-status-projected = Projected { $minutes } minutes. Check the queue chart.
cmu-queue-status-unstable = Line still unstable: { $nodes }.
cmu-queue-status-complete = Contract complete. { $reward } credits transferred to the colony budget.
cmu-queue-status-no-scenarios = No diagnostic contracts are configured.
cmu-queue-status-no-contract = Accept a contract first.
cmu-queue-status-unknown-node = Unknown node.
cmu-queue-status-error = Diagnostic error: { $error }.

cmu-queue-node-intake = Intake
cmu-queue-node-processor = Processor
cmu-queue-node-assembler = Assembler
cmu-queue-node-qc = Quality control
cmu-queue-node-packaging = Packaging

cmu-queue-network-tutorial-name = Bottleneck on the line
cmu-queue-network-tutorial-desc = A three-stage colony line is falling behind. One stage cannot keep up with the offered load. Find it, prove it with the formulas, and bring it back under capacity.
cmu-queue-network-rework-name = Rework loop
cmu-queue-network-rework-desc = A quality-control loop sends defective units back into the processor. The rework fraction raises the effective load on two stages at once. Find both bottlenecks before the line backs up.

cmu-queue-formulas-title = Queueing Reference
cmu-queue-formulas-heading = M/M/1 reference
cmu-queue-formulas-mm1 = M/M/1 formulas
cmu-queue-formulas-lambda = λ
cmu-queue-formulas-lambda-desc = Arrival rate, items per minute.
cmu-queue-formulas-mu = μ
cmu-queue-formulas-mu-desc = Service rate, items per minute (one server).
cmu-queue-formulas-rho = ρ = λ / μ
cmu-queue-formulas-rho-desc = Utilization: the fraction of time the server is busy.
cmu-queue-formulas-stable = Stable if ρ < 1
cmu-queue-formulas-stable-desc = If ρ ≥ 1, arrivals are at least as fast as service and the queue has no steady state.
cmu-queue-formulas-l = L = ρ / (1 − ρ)
cmu-queue-formulas-l-desc = Average number of items in the system.
cmu-queue-formulas-lq = Lq = ρ² / (1 − ρ)
cmu-queue-formulas-lq-desc = Average number of items waiting in the queue.
cmu-queue-formulas-w = W = 1 / (μ − λ)
cmu-queue-formulas-w-desc = Average time an item spends in the system, minutes.
cmu-queue-formulas-wq = Wq = λ / (μ(μ − λ))
cmu-queue-formulas-wq-desc = Average time an item spends waiting, minutes.
cmu-queue-formulas-negative = Why negative values appear
cmu-queue-formulas-negative-p1 = When ρ > 1, the denominator 1 − ρ is negative. L and Lq become negative, and so do W and Wq.
cmu-queue-formulas-negative-p2 = A negative queue length is impossible. The formula is telling you that no steady state exists: the backlog grows without bound until the line stops.
cmu-queue-formulas-negative-p3 = When ρ = 1 the denominator is zero, so the terminal shows ∞ instead of a negative number. The node is still unstable.
cmu-queue-formulas-network = Network flow balance
cmu-queue-formulas-network-equation = λ_i = γ_i + Σ_j λ_j · p_ji
cmu-queue-formulas-network-desc = γ_i is the external arrival rate at node i. p_ji is the fraction of node j's output routed to node i. Solve this for every node to get its offered load, then check ρ_i = λ_i / μ_i.
cmu-queue-formulas-example = Worked example
cmu-queue-formulas-example-body = A node has λ = 9 items/min and μ = 7 items/min. ρ = 9/7 = 1.29. L = 1.29/(1 − 1.29) = −4.45. The negative L is the overload alarm: the node needs μ > 9 items/min before a steady state exists.
cmu-queue-formulas-fix = How to fix a bottleneck
cmu-queue-formulas-fix-body = Analyze the suspected node to reveal λ and ρ. Upgrade its service rate until μ > λ, or reduce its incoming flow. The upgrade preview shows the new μ; analyze again or watch the projection to confirm the queue flattens.
