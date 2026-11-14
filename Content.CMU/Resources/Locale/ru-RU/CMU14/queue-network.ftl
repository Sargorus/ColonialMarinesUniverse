cmu-queue-console-title = Терминал диагностики производства
cmu-queue-console-heading = Диагностика производства
cmu-queue-console-subheading = Сеть массового обслуживания M/M/1
cmu-queue-console-budget = Ремонтный бюджет
cmu-queue-console-time = Время диагностики
cmu-queue-console-time-value = { $left }/{ $total } мин
cmu-queue-console-reward = Выплата по контракту
cmu-queue-console-reference = Справка
cmu-queue-console-idle = Активного контракта нет
cmu-queue-console-idle-desc = Возьмите контракт на диагностику производственной линии колонии. Каждый контракт даёт ремонтный бюджет и пятнадцать минут диагностики. Анализируйте узлы, находите те, у которых нет стационарного режима, повышайте интенсивность обслуживания или перенаправляйте поток и сдавайте линию, когда все очереди стабильны.
cmu-queue-console-request = Запросить контракт на диагностику
cmu-queue-console-overview = Контракт
cmu-queue-console-line = Производственная линия
cmu-queue-console-line-hint = Выберите узел, чтобы осмотреть его. Q — прогноз длины очереди.
cmu-queue-console-node-tooltip = Нажмите, чтобы осмотреть узел.
cmu-queue-console-node-label = { $selected }{ $name }  Q { $queue }
cmu-queue-console-service = Интенсивность обслуживания μ
cmu-queue-console-rate-range = Диапазон μ
cmu-queue-console-rate-range-value = { $base } – { $max }
cmu-queue-console-invested = Вложено
cmu-queue-console-full-upgrade = Полное улучшение
cmu-queue-console-external = Внешний поток
cmu-queue-console-queue = Прогноз очереди
cmu-queue-console-lambda = Интенсивность поступления λ
cmu-queue-console-rho = Загрузка ρ
cmu-queue-console-l = Среднее число в системе L
cmu-queue-console-lq = Средняя очередь Lq
cmu-queue-console-w = Среднее время в системе W
cmu-queue-console-wq = Среднее ожидание Wq
cmu-queue-console-no-data = Нет данных диагностики. Проанализируйте узел, чтобы узнать λ, ρ и стационарные значения.
cmu-queue-console-analyze = Анализировать узел ({ $cost } мин)
cmu-queue-console-stable-hint = Стационарный режим существует: ρ < 1. Узел справляется с потоком { $lambda }.
cmu-queue-console-unstable-hint = СТАЦИОНАРНОГО РЕЖИМА НЕТ: ρ ≥ 1. Очередь растёт неограниченно, а отрицательные L/Lq — это способ формул сказать об этом. Поднимите μ выше λ, перенаправьте поток или уменьшите входящий поток.
cmu-queue-console-set-rate = Применить μ
cmu-queue-console-set-rate-max = Максимум
cmu-queue-console-set-rate-cost = Стоимость: { $cost }
cmu-queue-console-set-rate-invalid = Введите значение выше текущей μ.
cmu-queue-console-routes = Маршруты
cmu-queue-console-routes-hint = Каждый узел отправляет эту долю своего выхода дальше. Всё, что не направлено в узел, покидает сеть.
cmu-queue-console-route-line = { $from } → { $to }: { $percent }%
cmu-queue-console-sink = отгрузка
cmu-queue-console-schematic = Схема сети
cmu-queue-console-schematic-empty = Нет данных сети.
cmu-queue-console-schematic-hint = Серый — узел не проанализирован, зелёный — стабилен, красный — перегружен. Пунктир — перенаправление потока. IN — внешний источник, OUT — отгрузка.
cmu-queue-console-projection = Прогноз очередей
cmu-queue-console-project = Прогноз на { $minutes } мин ({ $cost } мин диагностики)
cmu-queue-console-no-projection = Данных прогноза пока нет. Запустите прогноз, чтобы построить график очередей.
cmu-queue-console-projection-hint = Линия на нуле означает, что узел справляется. Растущая линия означает, что очередь переполняется.
cmu-queue-console-legend-entry = { $name }  Q { $queue }
cmu-queue-console-projection-caption = Длина очереди Q во времени, в минутах.
cmu-queue-console-bypasses = Перенаправление потока
cmu-queue-console-bypasses-hint = Перенаправьте часть внешнего потока с одного узла на другой. Это снижает нагрузку источника, но повышает нагрузку цели.
cmu-queue-console-bypass-route = Перенаправление
cmu-queue-console-bypass-progress = Текущее / максимум
cmu-queue-console-bypass-progress-value = { $current }% / { $max }%
cmu-queue-console-bypass-cost = Стоимость шага
cmu-queue-console-bypass-action = Перенаправить { $percent }% в { $to } ({ $cost })
cmu-queue-console-bypass-maxed = Достигнут максимум перенаправления
cmu-queue-console-actions = Действия по контракту
cmu-queue-console-submit = Сдать линию на проверку
cmu-queue-console-reset = Вернуть все улучшения
cmu-queue-console-new-contract = Запросить следующий контракт
cmu-queue-console-completed = Контракт выполнен
cmu-queue-console-completed-hint = Выплата { $reward } кредитов переведена в бюджет колонии.
cmu-queue-console-rate = { $value }/мин
cmu-queue-console-duration = { $value } мин
cmu-queue-console-rho-value = { $rho } ({ $percent }%)

cmu-queue-status-idle = Активного контракта нет.
cmu-queue-status-started = Контракт принят: { $network }.
cmu-queue-status-analyzed = Узел { $node } проанализирован: ρ = { $rho }.
cmu-queue-status-already-analyzed = Узел { $node } уже проанализирован.
cmu-queue-status-no-time = Не хватает времени диагностики для этого действия.
cmu-queue-status-rate-set = Узел { $node }: μ повышена до { $rate }/мин за { $cost } кредитов.
cmu-queue-status-rate-too-low = Целевая μ узла { $node } должна быть выше текущей.
cmu-queue-status-rate-invalid = Введите корректную целевую интенсивность обслуживания.
cmu-queue-status-no-budget = Не хватает ремонтного бюджета.
cmu-queue-status-rerouted = Поток { $from } перенаправлен: { $percent }% теперь идёт напрямую в { $to }.
cmu-queue-status-reroute-max = Перенаправление из { $from } в { $to } уже на максимуме.
cmu-queue-status-unknown-bypass = Неизвестное перенаправление.
cmu-queue-status-reset = Улучшения и перенаправления возвращены: { $refund } кредитов вернулось в ремонтный бюджет.
cmu-queue-status-reset-none = Нечего возвращать.
cmu-queue-status-projected = Построен прогноз на { $minutes } минут. Проверьте график очередей.
cmu-queue-status-unstable = Линия всё ещё нестабильна: { $nodes }.
cmu-queue-status-complete = Контракт выполнен. { $reward } кредитов переведено в бюджет колонии.
cmu-queue-status-no-contract = Сначала возьмите контракт.
cmu-queue-status-unknown-node = Неизвестный узел.
cmu-queue-status-error = Ошибка диагностики: { $error }.

cmu-queue-node-intake = Вход
cmu-queue-node-processor = Процессор
cmu-queue-node-assembler = Сборка
cmu-queue-node-qc = Контроль качества
cmu-queue-node-packaging = Упаковка
cmu-queue-node-merge = Слияние

cmu-queue-node-intake-short = Вход
cmu-queue-node-processor-short = Проц.
cmu-queue-node-assembler-short = Сборка
cmu-queue-node-qc-short = ОТК
cmu-queue-node-packaging-short = Упак.
cmu-queue-node-merge-short = Слияние

cmu-queue-topology-in = ВХОД
cmu-queue-topology-out = ВЫХОД
cmu-queue-topology-bypass = перенаправление
cmu-queue-topology-bypass-active = перенаправление { $percent }%
cmu-queue-topology-percent = { $percent }%
cmu-queue-topology-queue = Q { $queue }

cmu-queue-network-unknown = производственная линия
cmu-queue-network-tutorial-name = Узкое место на линии
cmu-queue-network-tutorial-desc = Простая линия колонии из трёх этапов отстаёт. Найдите этап, который не справляется, докажите это формулами и верните линию в норму.
cmu-queue-network-line-name = Узкое место линии
cmu-queue-network-line-desc = Линия колонии отстаёт. Один или несколько этапов не справляются с потоком. Найдите их, докажите это формулами и верните линию в норму.
cmu-queue-network-rework-name = Петля возврата
cmu-queue-network-rework-desc = Петля контроля качества возвращает брак обратно в линию. Доля возврата повышает нагрузку сразу на несколько этапов. Найдите все узкие места, пока линия не встала.
cmu-queue-network-branch-name = Ветвящаяся линия
cmu-queue-network-branch-desc = Вход делится между двумя ветками обработки и снова сливается. Одна ветка перегружена; можно перенаправить поток или улучшить этап, но каждое решение нагружает другую часть линии.
cmu-queue-network-split-name = Разделение и слияние
cmu-queue-network-split-desc = Две параллельные линии имеют общий вход и сливаются в конце. Узкое место может быть на любой ветке, а перенаправление потока меняет то, какая ветка страдает.
cmu-queue-formulas-title = Справочник по СМО
cmu-queue-formulas-heading = Справочник M/M/1
cmu-queue-formulas-intro = ρ — ключевое число: ниже 1 у узла есть стационарный режим; при 1 и выше очередь растёт неограниченно. Формулы ниже показывают почему.
cmu-queue-formulas-mm1 = Формулы M/M/1
cmu-queue-formulas-lambda = λ
cmu-queue-formulas-lambda-desc = Интенсивность поступления, единиц в минуту.
cmu-queue-formulas-mu = μ
cmu-queue-formulas-mu-desc = Интенсивность обслуживания, единиц в минуту (один сервер).
cmu-queue-formulas-rho = ρ = λ / μ
cmu-queue-formulas-rho-desc = Загрузка: доля времени, когда сервер занят.
cmu-queue-formulas-stable = Стабильно, если ρ < 1
cmu-queue-formulas-stable-desc = Если ρ ≥ 1, поток не меньше обслуживания и стационарного режима нет.
cmu-queue-formulas-l = L = ρ / (1 − ρ)
cmu-queue-formulas-l-desc = Среднее число единиц в системе.
cmu-queue-formulas-lq = Lq = ρ² / (1 − ρ)
cmu-queue-formulas-lq-desc = Среднее число единиц в очереди.
cmu-queue-formulas-w = W = 1 / (μ − λ)
cmu-queue-formulas-w-desc = Среднее время в системе, минуты.
cmu-queue-formulas-wq = Wq = λ / (μ(μ − λ))
cmu-queue-formulas-wq-desc = Среднее время ожидания, минуты.
cmu-queue-formulas-negative = Почему появляются отрицательные значения
cmu-queue-formulas-negative-p1 = При ρ > 1 знаменатель 1 − ρ отрицателен. L и Lq становятся отрицательными, как и W и Wq.
cmu-queue-formulas-negative-p2 = Отрицательной длины очереди не бывает. Формула говорит, что стационарного режима нет: очередь растёт без ограничения, пока линия не остановится.
cmu-queue-formulas-negative-p3 = При ρ = 1 знаменатель равен нулю, поэтому терминал показывает ∞. Узел всё равно нестабилен.
cmu-queue-formulas-network = Баланс потоков в сети
cmu-queue-formulas-network-equation = λ_i = γ_i + Σ_j λ_j · p_ji
cmu-queue-formulas-network-desc = γ_i — внешний поток в узел i. p_ji — доля выхода узла j, идущая в i. Решите систему для всех узлов, затем проверьте ρ_i = λ_i / μ_i.
cmu-queue-formulas-example = Пример
cmu-queue-formulas-example-body = Узел: λ = 9 единиц/мин, μ = 7 единиц/мин. ρ = 9/7 = 1.29. L = 1.29/(1 − 1.29) = −4.45. Отрицательное L — сигнал перегрузки: нужно μ > 9 единиц/мин.
cmu-queue-formulas-fix = Как убрать узкое место
cmu-queue-formulas-fix-body = Проанализируйте узел, чтобы узнать λ и ρ. Поднимите μ выше λ или перенаправьте часть потока. Прогноз покажет, выравнивается ли очередь.

ent-CMUProductionDiagnosticConsole = терминал диагностики производства
ent-CMUProductionDiagnosticConsole-desc = Терминал Weyland-Yutani для диагностики перегруженных производственных линий. Анализируйте узлы, находите те, у которых нет стационарного режима, повышайте интенсивность обслуживания или перенаправляйте поток и сдавайте линию, когда все очереди стабильны. Контракты пополняют бюджет колонии.
ent-CMUCrateProductionDiagnosticConsole = ящик с терминалом диагностики производства
ent-CMUCrateProductionDiagnosticConsole-desc = Запечатанный ящик с терминалом диагностики производства Weyland-Yutani. Вскройте его ломом.
