# Corporate liaison console tabs
cmu-corporate-tab-operations = Operations
cmu-corporate-tab-science = Science
cmu-corporate-tab-missions = Company Tasks
cmu-corporate-tab-pmc = PMC
cmu-corporate-tab-direction = Direction

# Science tab
cmu-corporate-science-heading = SCIENCE DIVISION
cmu-corporate-science-subheading = WEYLAND-YUTANI RESEARCH ACCOUNT // LIVE BALANCES
cmu-corporate-science-chemistry = Chemistry points
cmu-corporate-science-robotics = Robotics points
cmu-corporate-science-level = Science level
cmu-corporate-science-direction = MAIN DIRECTION
cmu-corporate-science-account = Research account
cmu-corporate-science-chemistry-hint = Chemistry points come from completed chemical contracts, scans and synthesis. They pay for chemistry work.
cmu-corporate-science-robotics-hint = Robotics points come from SMO contracts and completed drone build/repair orders. Robotics cartridges spend these points.
cmu-corporate-science-footer = Both balances are held by the WEYU research account and are shared by every science terminal.

# Company tasks tab
cmu-corporate-missions-heading = COMPANY TASKS
cmu-corporate-missions-subheading = WEYU COLONY LIAISON // MISSION BOARD
cmu-corporate-missions-active = Active missions
cmu-corporate-missions-available = Available missions
cmu-corporate-missions-history = Mission history
cmu-corporate-missions-empty = No company tasks are available right now.
cmu-corporate-missions-objective = Objective
cmu-corporate-missions-progress = Progress
cmu-corporate-missions-progress-value = {$progress} / {$target}
cmu-corporate-missions-time = Time left
cmu-corporate-missions-time-value = {$seconds} sec
cmu-corporate-missions-reward = Reward
cmu-corporate-missions-reward-value = {$reward} chemistry points
cmu-corporate-missions-accept = Accept mission
cmu-corporate-missions-status = Status
cmu-corporate-missions-failed = Failed

# Company task definitions
cmu-company-mission-robotics-name = Robotics science target
cmu-company-mission-robotics-desc = Build the complex's robotics science balance. SMO production contracts and completed drone work orders fund it.
cmu-company-mission-robotics-objective = Reach { $target } robotics points by minute { $minute }.
cmu-company-mission-chemistry-name = Chemistry science target
cmu-company-mission-chemistry-desc = Build the complex's chemistry science balance through contracts, scans and synthesis.
cmu-company-mission-chemistry-objective = Reach { $target } chemistry points by minute { $minute }.
cmu-company-mission-scan-simple-name = Simple property scan
cmu-company-mission-scan-simple-desc = Scan a substance whose properties are all common or uncommon. The sample must include at least one positive and one negative property, the antipodal pair the synthesis machine can transfer.
cmu-company-mission-scan-complex-name = Complex property scan
cmu-company-mission-scan-complex-desc = Scan a substance carrying at least one rare or legendary property.
cmu-company-mission-scan-objective = Complete the required scan by minute { $minute }.
cmu-company-mission-wendigo-name = Create a Wendigo
cmu-company-mission-wendigo-desc = Put a subject through the laboratory procedure. A lesser Wendigo pays half, a full MN-33 or cannibal-grown Wendigo pays full, and a tamed result pays double. Admin-spawned and round-threat Wendigos do not count.
cmu-company-mission-wendigo-objective = Create a lab-made Wendigo by minute { $minute }.
cmu-company-mission-hive-rep-name = Corrupted hive representative
cmu-company-mission-hive-rep-desc = Establish a living queen, hive leader or Overmind for a corrupted hive.
cmu-company-mission-hive-alliance-name = Corrupted hive alliance
cmu-company-mission-hive-alliance-desc = Secure an alliance between the corrupted hive and Weyland-Yutani. The hive must list WEYU as an ally or the Confluence faction must be friendly to WEYU.
cmu-company-mission-single-objective = Complete this task by minute { $minute }.
cmu-company-mission-cuffs-name = Unlock predator restraints
cmu-company-mission-cuffs-desc = Remove Yautja restraints from their captives. Every restraint removed pays one point.
cmu-company-mission-cuffs-objective = Remove { $target } Yautja restraints by minute { $minute }.

# Mission state messages
cmu-company-mission-accepted = Accepted { $name }.
cmu-company-mission-completed = Completed { $name }: +{ $reward } chemistry points.
cmu-company-mission-failed = { $name } expired before completion.
cmu-company-mission-already-done = That mission is already closed.
cmu-company-mission-already-active = That mission is already active.
cmu-company-mission-limit = The liaison can run at most { $limit } company missions at once.
cmu-company-mission-too-late = The deadline for that mission has passed (minute { $minute }).

# Main direction
cmu-company-direction-none = Not chosen
cmu-company-direction-robotics = Robotics
cmu-company-direction-bioengineering = Bioengineering (Chemistry)
cmu-company-direction-set = Main direction set to { $direction }.

# PMC tab
cmu-corporate-pmc-heading = PRIVATE MILITARY CONTRACTORS
cmu-corporate-pmc-subheading = WEYU PMC // CONTRACT SUPPORT
cmu-corporate-pmc-cost = Chemistry cost
cmu-corporate-pmc-calls = Calls left
cmu-corporate-pmc-contract = Contract
cmu-corporate-pmc-description = Hire a Weyland-Yutani PMC fireteam for corporate security, recovery or deniable operations. The squad deploys by shuttle and answers to the liaison.
cmu-corporate-pmc-call = Request PMC squad
cmu-corporate-pmc-confirm = CONFIRM CALL
cmu-corporate-pmc-footer = PMC calls are paid from chemistry science points and are limited per round.
cmu-corporate-pmc-no-calls = No PMC calls remain this round.
cmu-corporate-pmc-cooldown = Next PMC squad available in { $seconds } sec.
cmu-corporate-pmc-ready = PMC squad ready for tasking.

# Direction tab
cmu-corporate-direction-heading = COMPLEX DIRECTION
cmu-corporate-direction-subheading = WEYU COLONY LIAISON // RESEARCH PRIORITY
cmu-corporate-direction-current = Current direction
cmu-corporate-direction-warning = The main direction is committed for the round and shapes which research recipes the complex can buy.
cmu-corporate-direction-future = Future research access
cmu-corporate-direction-future-hint = Robotics and bioengineering recipe pools will be gated by this choice.
cmu-corporate-direction-confirm = CONFIRM DIRECTION
cmu-corporate-direction-footer = Choose the complex's main science direction.
cmu-corporate-direction-robotics = ROBOTICS
cmu-corporate-direction-bioengineering = BIOENGINEERING
