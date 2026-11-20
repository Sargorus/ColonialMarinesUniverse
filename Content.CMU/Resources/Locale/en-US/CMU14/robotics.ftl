ent-CMURoboticsFabricator = robotics fabricator
    .desc = A Weyland-Yutani fabricator for android frames, limbs, control tablets, modules and activation keys. It needs a WL-6 universal fuel cell and a trained roboticist.

ent-CMUCrateRoboticsFabricator = robotics fabricator crate
    .desc = A sealed crate containing one Weyland-Yutani robotics fabricator. The fuel cell is not included; install a WL-6 universal fuel cell to power it. Pry it open with a crowbar.

ent-CMURoboticsOrderConsole = robotics order console
    .desc = A Weyland-Yutani contract terminal for robotics work. Accept a build or repair order, receive its synthetic transport crates in cargo, then attach the order slip to a loaded crate and lower the platform. It can be anchored, moved and rotated.

ent-CMUCrateRoboticsOrderConsole = robotics order console crate
    .desc = A sealed crate containing one Weyland-Yutani robotics order console. Pry it open with a crowbar.

ent-CMUCrateSyntheticTransport = synthetic transport crate
    .desc = A lockable transport crate sized for one synthetic and one drone control tablet. Attach a robotics order slip to its label slot before lowering it with the cargo platform.

ent-CMUMedibot = medical bot
    .desc = A Weyland-Yutani medical bot. It injects the contents of its beaker and performs CPR on intact corpses. Its beaker slot is behind a screw-fastened service panel.

ent-CMUCleanBot = cleaner bot
    .desc = A Weyland-Yutani cleaner bot. It mops puddles and follows blood trails left by dragging a bleeding body.

ent-CMUHugBot = hug bot
    .desc = A Weyland-Yutani hug bot. It wanders the colony looking for someone who needs a hug.

ent-CMURoboticsOrderSlip = robotics supply order
    .desc = A supply order form for a Weyland-Yutani robotics contract. The order details are printed on the sheet.

ent-CMUWorkingJoeRepair = damaged Working Joe
    .desc = A soulless Working Joe sent for repair. It is missing limbs and has burn and blunt damage.

ent-CMUCombatDrone = gun UGV
    .desc = A remotely operated tracked UGV with an IFF-equipped pulse gun and a 180-degree forward firing arc.

ent-CMUFlamerDrone = flamer UGV
    .desc = A remotely operated tracked UGV with twin claw-mounted flame nozzles.

ent-CMUDroneAndroidUnarmed = civilian drone
    .desc = A stripped-down synthetic drone frame with exposed servos. Its control bus is deliberately locked out of firearm use.

ent-CMUDroneAndroidUnarmedRepair = damaged civilian drone
    .desc = A repair-order civilian drone. It is missing limbs and has burn and blunt damage.

ent-CMUDroneAndroidCombatRepair = damaged combat drone
    .desc = A repair-order combat drone. It is missing limbs and has burn and blunt damage.

ent-CMUDroneAndroidCombat = combat drone
    .desc = A reinforced synthetic drone frame with a combat-rated control bus and weapon routines.

ent-CMUDroneBodyFrameUnarmed = civilian humanoid drone body frame
    .desc = A bare synthetic torso frame whose control bus is locked out of firearm use. Its limb ports are sealed and its activation socket is empty.

ent-CMUDroneBodyFrameCombat = combat humanoid drone body frame
    .desc = A reinforced synthetic torso frame with a combat-rated control bus. Its limb ports are sealed and its activation socket is empty.

ent-CMUDroneModuleCombat = drone combat module
    .desc = A tactical combat module loaded with close-quarters and firearms routines.

ent-CMUDroneModuleMedical = drone medical module
    .desc = A medical module loaded with doctor-grade treatment and surgery routines.

ent-CMUDroneModuleEngineering = drone engineering module
    .desc = An engineering module loaded with construction, engineering, and power loader routines.

ent-CMUDroneModulePolice = drone police module
    .desc = A police module loaded with law enforcement, endurance, hand-to-hand, and melee routines. It does not grant firearm authorization.

ent-CMUCrateCombatDroneFrame = combat drone body frame crate
    .desc = A sealed crate containing one combat humanoid drone body frame. Its limb ports are sealed and its activation socket is empty.

ent-CMUCrateDroneOutfitEngineering = drone engineering outfit crate
    .desc = A crate containing a Weyland-Yutani engineering jumpsuit, an engineer oversuit, a filled M276 toolbelt, and a PMC technician welderpack.
ent-CMUCrateDroneOutfitMedical = drone medical outfit crate
    .desc = A crate containing orange medical scrubs, a white lab coat, an empty medical belt, a medical backpack, and an orange surgical cap.
ent-CMUCrateDroneOutfitPolice = drone police outfit crate
    .desc = A crate containing a Weyland-Yutani corporate security uniform, a lightweight combat pack, and a corporate security rig loaded with non-lethal equipment.
ent-CMUCrateDroneArmorM4 = corporate M4 vest crate
    .desc = A crate containing one corporate-pattern M4 armor vest for a drone frame.

ent-CMUCrateMagazineM96S = M96S magazine crate (1x)
    .desc = A crate containing one M96S sniper rifle magazine.

ent-CMUCrateMagazineML66A = ML66A OBD drum magazine crate (1x, 10x30mm)
    .desc = A crate containing one ML66A OBD smart gun drum magazine in 10x30mm.

cmu-lathe-category-robotics-synth-limbs = Synthetic limbs
cmu-lathe-category-robotics-working-joe-limbs = Working Joe limbs
cmu-lathe-category-robotics-prosthetics = Human prosthetics
cmu-lathe-category-robotics-components = Components
cmu-lathe-category-robotics-bots = Bots

cmu-robotics-fabricator-cell-already = The fabricator already has a fuel cell.
cmu-robotics-fabricator-cell-insert-start = You start inserting the fuel cell into the fabricator.
cmu-robotics-fabricator-cell-insert-fail = The fuel cell does not fit.
cmu-robotics-fabricator-cell-insert-finish = You insert the fuel cell into the fabricator.
cmu-robotics-fabricator-cell-none = The fabricator has no fuel cell to remove.
cmu-robotics-fabricator-cell-remove-start = You start removing the fuel cell from the fabricator.
cmu-robotics-fabricator-cell-remove-verb = Remove fuel cell
cmu-robotics-fabricator-cell-remove-finish = You remove the fuel cell from the fabricator.
cmu-robotics-fabricator-examine-cell-inserted = The fuel cell is [color=cyan]inserted[/color].
cmu-robotics-fabricator-examine-cell-empty = The fuel cell is [color=red]not inserted[/color].
cmu-robotics-fabricator-examine-research = Requires [color=cyan]Research {$level}[/color].
cmu-robotics-fabricator-skill-required = You need Research {$level} to operate this fabricator.

cmu-robotics-console-title = Robotics Order Console
cmu-robotics-console-heading = ROBOTICS ORDERS
cmu-robotics-console-subheading = Weyland-Yutani contract terminal
cmu-robotics-console-active = Active order
cmu-robotics-console-available = Available orders
cmu-robotics-console-no-active = No order accepted.
cmu-robotics-console-no-orders = No orders available.
cmu-robotics-console-active-exists = Finish or cancel the current order first.
cmu-robotics-console-accepted-repair = Order accepted: {$target}. {$crates} synthetic transport crates with {$amount} damaged units were added to the cargo queue. An order slip was printed; raise the platform (lower and raise it if already raised) to receive the crates, then put each repaired unit into a crate, attach the slip to a loaded crate, and lower the platform. Reward: {$reward}.
cmu-robotics-console-accepted-build = Order accepted: {$target}. {$crates} empty synthetic transport crate was added to the cargo queue. An order slip was printed; raise the platform (lower and raise it if already raised) to receive the crate, put the finished unit and a drone control tablet inside, attach the slip to the crate, and lower the platform. Reward: {$reward}.
cmu-robotics-console-no-queue-space = The order cannot be accepted: the cargo queue does not have enough space (needs {$needed}, {$free} free).
cmu-robotics-console-no-platform = No corporate cargo platform was found.
cmu-robotics-console-rejected = The customer is not satisfied: {$reason}. No payment was made.
cmu-robotics-console-delivered = Order successfully delivered: {$target}. {$reward} was transferred to the corporate affairs console account.
cmu-robotics-console-delivered-penalty = Order delivered with a deduction: {$target}. {$reward} was transferred to the corporate affairs console account; missing elements: {$missing}, penalty: {$penalty}.
cmu-robotics-console-penalty = Penalty
cmu-robotics-console-penalty-none = No missing elements; no penalty.
cmu-robotics-console-penalty-value = Missing elements: {$missing}; penalty: -{$penalty}.
cmu-robotics-console-cancelled = Order cancelled.
cmu-robotics-console-print-slip = Print order slip
cmu-robotics-console-slip-printed = Order slip printed.
cmu-robotics-slip-attach = Attach order slip
cmu-robotics-slip-remove = Remove order slip
cmu-robotics-console-time-left = Time left
cmu-robotics-console-failed-timeout = The order deadline expired. A fine of {$fine} (25% of the reward) was charged to the corporate affairs console account.
cmu-robotics-console-refresh-active = You cannot refresh the list while an order is active.
cmu-robotics-console-refreshed = Order list refreshed.
cmu-robotics-console-type = Type
cmu-robotics-console-target = Target
cmu-robotics-console-module = Required module
cmu-robotics-console-amount = Units
cmu-robotics-console-reward = Reward
cmu-robotics-console-type-build = Build
cmu-robotics-console-type-repair = Repair
cmu-robotics-console-order-heading = {$type}: {$target}
cmu-robotics-console-accept = Accept order
cmu-robotics-console-cancel = Cancel
cmu-robotics-console-refresh = Refresh list
cmu-robotics-console-delivery-hint-repair = Put each repaired unit into a synthetic transport crate, attach the order slip to a loaded crate, and lower the cargo platform.
cmu-robotics-console-delivery-hint-build = Put the finished unit and a drone control tablet into a synthetic transport crate, attach the order slip to it, and lower the cargo platform.
cmu-robotics-console-reason-wrong = this is not the ordered unit
cmu-robotics-console-reason-broken = the unit is broken or damaged
cmu-robotics-console-reason-no-slip = the order slip is not attached to a transport crate
cmu-robotics-console-reason-slip-not-on-crate = the order slip must be attached to a transport crate that contains an order unit
cmu-robotics-slip-name = robotics work order #{$id}
cmu-robotics-target-civilian-drone = Civilian drone
cmu-robotics-target-combat-drone = Combat drone
cmu-robotics-target-working-joe = Working Joe
cmu-robotics-slip-module-none = not required
cmu-robotics-slip-package-build = finished unit + drone control tablet
cmu-robotics-slip-package-repair = repaired unit
cmu-robotics-slip-content = [bold]WEYLAND-YUTANI SUPPLY ORDER #{$id}[/bold]\n\nForm: robotics supply order\nOrder type: {$type}\nTarget: {$target}\nRequired module: {$module}\nUnits: {$amount}\nPackage: {$package}\nReward: {$reward}\nTime limit: {$minutes} minutes\n\nAcceptance: install the required module in the target, place the unit in a synthetic transport crate, attach this form to the loaded crate, and lower the corporate cargo platform.
