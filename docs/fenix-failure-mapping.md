# Fenix failure mapping (FailureKey ↔ Fenix EFB id)

Reference for the future `fenix_failure_id → failure_key` migration of fenixhangarweb and of the applications
([ADR 0002](decisions/0002-server-failure-key.md)). **Nothing here changes the server.** The raw Fenix id appears in
this document and in FSGAP.Fenix's internal resources only, never in a public FSGAP type.

- Source of truth: `src/FSGAP.Fenix/Failures/Resources/fenix-failure-mapping.json` (normalized keys) and
  `fenix-failure-catalog.json` (the 384-entry EFB list). The table below is generated from the former.
- Scope: the 40 failures a consumer actually uses today. The other 344 Fenix failures have no key yet (see
  [fenix-failures.md](fenix-failures.md#catalogue-and-key-policy)).
- Every key supports **trigger and clear**. The command's target must be the listed target (for example
  `new FailureCommand(FailureKey.Parse("engine.1.surge"), FailureTarget.Engine(1))`).
- Target ids reuse the telemetry ids: fuel pump `left-1`, hydraulic system `blue`, and so on.

| FailureKey | Semantic meaning | Current Fenix id | Target | Current users |
|---|---|---|---|---|
| `air-conditioning.cpc.1` | Cabin pressure controller 1 | `F_PNEUMATIC_CPC_1` | Aircraft | FSHANGAR live verification and tests |
| `air-conditioning.pack.1.overheat` | Pack 1 overheat | `F_PNEUMATIC_PACK_1_OVERHEAT` | Aircraft | FLIPPP Tricky, fenixhangarweb tests |
| `air-conditioning.pack.1.regulator-fault` | Pack 1 regulator fault | `F_PNEUMATIC_PACK_1_REG_FAULT` | Aircraft | fenixhangarweb tests |
| `electrical.static-inverter` | Static inverter | `F_ELEC_STATIC_INVERTER` | Aircraft | FLIPPP Easy |
| `electrical.generator.1` | Generator 1 (IDG 1 drive) | `F_ELEC_DRIVE_FAILURE_L` | Aircraft | FLIPPP Tricky |
| `electrical.generator.2` | Generator 2 (IDG 2 drive) | `F_ELEC_DRIVE_FAILURE_R` | Aircraft | FLIPPP Tricky |
| `electrical.bus.ac-1` | AC bus 1 | `F_ELEC_BUS_AC_1` | ElectricalBus `ac-1` | FLIPPP Reckless, fenixhangarweb component aliases |
| `electrical.bus.ac-ess` | AC essential bus | `F_ELEC_BUS_AC_ESSENTIAL` | ElectricalBus `ac-ess` | fenixhangarweb component aliases |
| `electrical.bus.dc-1` | DC bus 1 | `F_ELEC_BUS_DC_1` | ElectricalBus `dc-1` | FSHANGAR tests, fenixhangarweb component aliases |
| `electrical.bus.dc-2` | DC bus 2 | `F_ELEC_BUS_DC_2` | ElectricalBus `dc-2` | FSHANGAR tests, fenixhangarweb component aliases |
| `electrical.bus.dc-bat` | DC battery bus | `F_ELEC_BUS_BAT` | ElectricalBus `dc-bat` | fenixhangarweb component aliases |
| `fire.lavatory.smoke` | Lavatory smoke | `F_FIRE_LAVATORY_SMOKE` | Aircraft | FLIPPP Tricky |
| `fire.engine.1.loop-a` | Engine 1 fire detection loop A | `F_OH_FIRE_ENG1_LOOP_A` | Engine 1 | FLIPPP Reckless, FSHANGAR fire-probe trials |
| `fire.fdu.1` | Fire detection unit 1 | `F_FIRE_FDU1` | Aircraft | FSHANGAR fire-probe experiment (paused, never injected) |
| `fuel.fqi.channel-2` | Fuel quantity indication channel 2 | `F_FUEL_FQI2` | Aircraft | FLIPPP Easy |
| `fuel.pump.left-1` | Left tank fuel pump 1 | `F_FUEL_PUMP_LEFT_1` | FuelPump `left-1` | fenixhangarweb component aliases |
| `fuel.pump.right-1` | Right tank fuel pump 1 | `F_FUEL_PUMP_RIGHT_1` | FuelPump `right-1` | fenixhangarweb component aliases |
| `hydraulic.blue.electric-pump` | Blue electric hydraulic pump | `F_HYD_PUMP_BLUE` | HydraulicSystem `blue` | FLIPPP Tricky |
| `hydraulic.yellow.electric-pump` | Yellow electric hydraulic pump | `F_HYD_PUMP_YELLOW` | HydraulicSystem `yellow` | FLIPPP Tricky |
| `hydraulic.blue.low-level` | Blue hydraulic reservoir low level | `F_HYD_LOW_BLUE` | HydraulicSystem `blue` | FLIPPP Tricky |
| `hydraulic.green.low-level` | Green hydraulic reservoir low level | `F_HYD_LOW_GREEN` | HydraulicSystem `green` | fenixhangarweb component aliases |
| `hydraulic.blue.leak` | Blue hydraulic leak | `F_HYD_LEAK_BLUE` | HydraulicSystem `blue` | FLIPPP Reckless |
| `hydraulic.green.leak` | Green hydraulic leak | `F_HYD_LEAK_GREEN` | HydraulicSystem `green` | fenixhangarweb component aliases |
| `ice-rain.aoa-heat.standby` | Standby AOA probe heat | `F_ICE_AOA_STBY` | Aircraft | FLIPPP Easy |
| `ice-rain.pitot-heat.fo` | First officer pitot heat | `F_ICE_PITOT_FO` | Aircraft | FLIPPP Tricky |
| `indicating.display.ecam-lower` | Lower ECAM display unit | `F_DISPLAY_DU_ECAM_LOWER` | Aircraft | FLIPPP Easy |
| `landing-gear.brake.wheel-1` | Wheel 1 brake fault | `F_BRAKE_WHEEL_1` | Aircraft | FLIPPP Tricky |
| `landing-gear.tyre-pressure.main-1` | Main tyre 1 low pressure | `F_GEAR_TYRE_PSI_MAIN_1` | Aircraft | FLIPPP Reckless |
| `landing-gear.tyre-pressure.right-1` | Right tyre 1 low pressure | `F_GEAR_TYRE_PSI_RIGHT_1` | Aircraft | FSHANGAR live verification and tests |
| `navigation.fmgc.1` | FMGC 1 | `F_FMGC_1` | Aircraft | FLIPPP Easy |
| `navigation.mcdu.1.recoverable-fault` | MCDU 1 recoverable fault | `F_MCDU_1_RECOVERABLE` | Aircraft | FLIPPP Easy |
| `navigation.adf.1` | ADF 1 | `F_NAV_ADF1` | Aircraft | FLIPPP Easy |
| `navigation.gps.1` | GPS 1 | `F_NAV_GPS1` | Aircraft | FLIPPP Easy |
| `navigation.ils.1.localizer` | ILS 1 localizer | `F_NAV_ILS1_LOC` | Aircraft | FLIPPP Tricky |
| `pneumatic.bleed-valve.1` | Engine 1 bleed valve | `F_PNEUMATIC_BLEED_VALVE_1` | Aircraft | FSHANGAR live verification and tests, fenixhangarweb API docs example |
| `doors.entry.forward-left` | Forward left entry door | `F_DOOR_FWD_ENTRY_LEFT` | Aircraft | fenixhangarweb component aliases |
| `doors.entry.aft-left` | Aft left entry door | `F_DOOR_AFT_ENTRY_LEFT` | Aircraft | fenixhangarweb component aliases |
| `engine.1.surge` | Engine 1 surge | `F_ENGINE_1_SURGE` | Engine 1 | FLIPPP Reckless |
| `engine.1.vibration.n1` | Engine 1 high N1 vibration | `F_VIB_N1_ENG_1` | Engine 1 | FLIPPP Reckless |
| `engine.1.eiu` | Engine 1 interface unit (EIU 1) | `F_ENG1_EIU` | Engine 1 | FSHANGAR fire-probe trials |

## Notes for the server migration

- **What fenixhangarweb stores today (read-only audit, 2026-09-24).**
  - The first seed migrations of `fenix_failures` and `component_failure_modes` use **placeholder ids that are not
    Fenix ids**: `ENG_1_FAIL`, `APU_FAIL`, `HYD_G_PUMP_FAIL`, `IDG_1_FAIL`, `ENG_1_OIL_PRESS`, and so on. The
    FSHANGAR client refuses any id missing from its local catalog, so such rows could never be injected.
  - Real Fenix ids appear in `src/lib/fh/scenario/component-aliases.ts`, in tests and in the API docs example; all
    of them are in the table above.
  - The live contents of the production tables (admin-editable) were not inspected. Any id found there that is not
    in this table needs a key first.
- **Migration rule.**
  - Replace each `fenix_failure_id` with its `failure_key` from this table.
  - For an id with no key: add it to `fenix-failure-mapping.json` first, following the key policy, with a
    test-backed review. Never invent a key on the server side.
- **Targets.** A key carries its instance, and the command target must match the listed target. The server can
  store the key alone: a client finds the target in the session's catalog (`FailureDefinition.SupportedTargets`).
- **FLIPPP.** Its 24 scenario ids are all mapped. Its `CompatibilityGroup` values (for example `HYD_BLUE` shared by
  `hydraulic.blue.electric-pump`, `hydraulic.blue.low-level` and `hydraulic.blue.leak`) are application logic and
  stay in FLIPPP.
