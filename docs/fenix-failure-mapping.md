# Fenix failure mapping (FailureKey ↔ Fenix EFB id)

Reference for the future `fenix_failure_id → failure_key` migration of fenixhangarweb and of the applications
([ADR 0002](decisions/0002-server-failure-key.md)). **Nothing here changes the server.** The raw Fenix id appears in
this document and in FSGAP.Fenix's internal resources only, never in a public FSGAP type.

- Source of truth: `src/FSGAP.Fenix/Failures/Resources/fenix-failure-mapping.json` (normalized keys) and
  `fenix-failure-catalog.json` (the 384-entry EFB list). The table below is generated from the former.
- Scope: **all 384** Fenix EFB failures (since 0.12.0-preview.4). The first 40 were normalized in BLOCK 7 for the
  consumers that used them (they keep their keys, names and targets unchanged); the other 344 in BLOCK 12.2, by the
  same key policy (see [fenix-failures.md](fenix-failures.md#catalogue-and-key-policy)). Rows are in EFB catalog order.
- Every key supports **trigger and clear**. The command's target must be the listed target (for example
  `new FailureCommand(FailureKey.Parse("engine.1.surge"), FailureTarget.Engine(1))`).
- Target ids reuse the telemetry ids: fuel pump `left-1`, hydraulic system `blue`, and so on.

| FailureKey | Semantic meaning | Current Fenix id | ATA | Target | Normalized in | Current users |
|---|---|---|---|---|---|---|
| `air-conditioning.cpc.1` | Cabin pressure controller 1 | `F_PNEUMATIC_CPC_1` | 21 | Aircraft | 0.9 (BLOCK 7) | FSHANGAR live verification and tests |
| `air-conditioning.cpc.2` | Cabin pressure controller 2 | `F_PNEUMATIC_CPC_2` | 21 | Aircraft | 0.12.0-preview.4 | — |
| `air-conditioning.pack.1.overheat` | Pack 1 overheat | `F_PNEUMATIC_PACK_1_OVERHEAT` | 21 | Aircraft | 0.9 (BLOCK 7) | FLIPPP Tricky, fenixhangarweb tests |
| `air-conditioning.pack.2.overheat` | Pack 2 overheat | `F_PNEUMATIC_PACK_2_OVERHEAT` | 21 | Aircraft | 0.12.0-preview.4 | — |
| `air-conditioning.pack.1.regulator-fault` | Pack 1 regulator fault | `F_PNEUMATIC_PACK_1_REG_FAULT` | 21 | Aircraft | 0.9 (BLOCK 7) | fenixhangarweb tests |
| `air-conditioning.pack.2.regulator-fault` | Pack 2 regulator fault | `F_PNEUMATIC_PACK_2_REG_FAULT` | 21 | Aircraft | 0.12.0-preview.4 | — |
| `air-conditioning.zone-controller.primary` | Zone controller primary channel | `F_PNEUMATIC_ZONECONTROLLER_PRIM` | 21 | Aircraft | 0.12.0-preview.4 | — |
| `air-conditioning.zone-controller.secondary` | Zone controller secondary channel | `F_PNEUMATIC_ZONECONTROLLER_SEC` | 21 | Aircraft | 0.12.0-preview.4 | — |
| `air-conditioning.trim-air.hot-air-fault` | Trim air hot air fault | `F_PNEUMATIC_TRIM_AIR` | 21 | Aircraft | 0.12.0-preview.4 | — |
| `air-conditioning.cargo-ventilation-controller` | Cargo ventilation controller | `F_PNEUMATIC_CVC` | 21 | Aircraft | 0.12.0-preview.4 | — |
| `air-conditioning.cargo.aft.hot-air-valve` | Aft cargo hot air valve | `F_PNEUMATIC_HOT_AIR_VALVE_AFT_CARGO` | 21 | Aircraft | 0.12.0-preview.4 | — |
| `air-conditioning.cargo.forward.isolation-valve.upstream` | Forward cargo isolation valve, upstream | `F_PNEUMATIC_CARGO_ISOL_FWD_UP` | 21 | Aircraft | 0.12.0-preview.4 | — |
| `air-conditioning.cargo.forward.isolation-valve.downstream` | Forward cargo isolation valve, downstream | `F_PNEUMATIC_CARGO_ISOL_FWD_DOWN` | 21 | Aircraft | 0.12.0-preview.4 | — |
| `air-conditioning.cargo.aft.isolation-valve.upstream` | Aft cargo isolation valve, upstream | `F_PNEUMATIC_CARGO_ISOL_AFT_UP` | 21 | Aircraft | 0.12.0-preview.4 | — |
| `air-conditioning.cargo.aft.isolation-valve.downstream` | Aft cargo isolation valve, downstream | `F_PNEUMATIC_CARGO_ISOL_AFT_DOWN` | 21 | Aircraft | 0.12.0-preview.4 | — |
| `air-conditioning.recirculation-fans` | Recirculation fans | `F_PNEUMATIC_RECIRC_FANS` | 21 | Aircraft | 0.12.0-preview.4 | — |
| `air-conditioning.decompression.slow` | Slow decompression | `F_PNEUMATIC_DECOMPRESSION_MINOR` | 21 | Aircraft | 0.12.0-preview.4 | — |
| `air-conditioning.decompression.rapid` | Rapid decompression | `F_PNEUMATIC_DECOMPRESSION_MAJOR` | 21 | Aircraft | 0.12.0-preview.4 | — |
| `air-conditioning.outflow-valve.stuck` | Outflow valve stuck | `F_PNEUMATIC_OUTFLOWVALVE_STUCK` | 21 | Aircraft | 0.12.0-preview.4 | — |
| `air-conditioning.ventilation.aevc` | Avionics equipment ventilation controller | `F_PNEUMATIC_AEVC` | 21 | Aircraft | 0.12.0-preview.4 | — |
| `air-conditioning.ventilation.blower` | Ventilation blower fault | `F_PNEUMATIC_BLOWER` | 21 | Aircraft | 0.12.0-preview.4 | — |
| `air-conditioning.ventilation.extract` | Ventilation extract fault | `F_PNEUMATIC_EXTRACT` | 21 | Aircraft | 0.12.0-preview.4 | — |
| `air-conditioning.ventilation.inlet-valve` | Ventilation inlet valve | `F_PNEUMATIC_VENT_INLET` | 21 | Aircraft | 0.12.0-preview.4 | — |
| `air-conditioning.ventilation.extract-valve` | Ventilation extract valve | `F_PNEUMATIC_VENT_EXTRACT` | 21 | Aircraft | 0.12.0-preview.4 | — |
| `auto-flight.fac.1` | FAC 1 | `F_OH_FLT_CTL_FAC_1` | 22 | Aircraft | 0.12.0-preview.4 | — |
| `auto-flight.fac.2` | FAC 2 | `F_OH_FLT_CTL_FAC_2` | 22 | Aircraft | 0.12.0-preview.4 | — |
| `auto-flight.fac.1.resettable-fault` | FAC 1 resettable fault | `F_OH_FLT_CTL_FAC_RECOVERABLE_1` | 22 | Aircraft | 0.12.0-preview.4 | — |
| `auto-flight.fac.2.resettable-fault` | FAC 2 resettable fault | `F_OH_FLT_CTL_FAC_RECOVERABLE_2` | 22 | Aircraft | 0.12.0-preview.4 | — |
| `auto-flight.rudder-travel-limiter.1` | Rudder travel limiter channel 1 | `F_FC_YAW_RTL_1` | 22 | Aircraft | 0.12.0-preview.4 | — |
| `auto-flight.rudder-travel-limiter.2` | Rudder travel limiter channel 2 | `F_FC_YAW_RTL_2` | 22 | Aircraft | 0.12.0-preview.4 | — |
| `auto-flight.rudder-trim.1` | Rudder trim channel 1 | `F_FC_RUDDERTRIM_1` | 22 | Aircraft | 0.12.0-preview.4 | — |
| `auto-flight.rudder-trim.2` | Rudder trim channel 2 | `F_FC_RUDDERTRIM_2` | 22 | Aircraft | 0.12.0-preview.4 | — |
| `auto-flight.windshear-detection.1` | Reactive windshear detection channel 1 | `F_FC_WINDSHEAR_1` | 22 | Aircraft | 0.12.0-preview.4 | — |
| `auto-flight.windshear-detection.2` | Reactive windshear detection channel 2 | `F_FC_WINDSHEAR_2` | 22 | Aircraft | 0.12.0-preview.4 | — |
| `auto-flight.law.alternate-protected` | Alternate law with protection | `F_FC_ALTERNATE_LAW_1` | 22 | Aircraft | 0.12.0-preview.4 | — |
| `auto-flight.law.alternate-unprotected` | Alternate law without protection | `F_FC_ALTERNATE_LAW_2` | 22 | Aircraft | 0.12.0-preview.4 | — |
| `auto-flight.law.direct` | Direct law | `F_FC_DIRECT_LAW` | 22 | Aircraft | 0.12.0-preview.4 | — |
| `auto-flight.fcu.channel-1` | FCU channel 1 | `F_FCU_1` | 22 | Aircraft | 0.12.0-preview.4 | — |
| `auto-flight.fcu.channel-2` | FCU channel 2 | `F_FCU_2` | 22 | Aircraft | 0.12.0-preview.4 | — |
| `auto-flight.autothrust.1` | Autothrust 1 | `F_AUTOFLIGHT_ATHR1` | 22 | Aircraft | 0.12.0-preview.4 | — |
| `auto-flight.autothrust.2` | Autothrust 2 | `F_AUTOFLIGHT_ATHR2` | 22 | Aircraft | 0.12.0-preview.4 | — |
| `auto-flight.autopilot.1` | Autopilot 1 | `F_AUTOFLIGHT_AP1` | 22 | Aircraft | 0.12.0-preview.4 | — |
| `auto-flight.autopilot.2` | Autopilot 2 | `F_AUTOFLIGHT_AP2` | 22 | Aircraft | 0.12.0-preview.4 | — |
| `electrical.bus.dc-bat` | DC battery bus | `F_ELEC_BUS_BAT` | 24 | ElectricalBus `dc-bat` | 0.9 (BLOCK 7) | fenixhangarweb component aliases |
| `electrical.bus.dc-1` | DC bus 1 | `F_ELEC_BUS_DC_1` | 24 | ElectricalBus `dc-1` | 0.9 (BLOCK 7) | FSHANGAR tests, fenixhangarweb component aliases |
| `electrical.bus.dc-2` | DC bus 2 | `F_ELEC_BUS_DC_2` | 24 | ElectricalBus `dc-2` | 0.9 (BLOCK 7) | FSHANGAR tests, fenixhangarweb component aliases |
| `electrical.bus.hot-1` | Hot bus 1 | `F_ELEC_BUS_HOT_1` | 24 | ElectricalBus `hot-1` | 0.12.0-preview.4 | — |
| `electrical.bus.hot-2` | Hot bus 2 | `F_ELEC_BUS_HOT_2` | 24 | ElectricalBus `hot-2` | 0.12.0-preview.4 | — |
| `electrical.bus.dc-ess` | DC essential bus | `F_ELEC_BUS_DC_ESSENTIAL` | 24 | ElectricalBus `dc-ess` | 0.12.0-preview.4 | — |
| `electrical.bus.dc-ess-shed` | DC essential shed bus | `F_ELEC_BUS_DC_SHED` | 24 | ElectricalBus `dc-ess-shed` | 0.12.0-preview.4 | — |
| `electrical.bus.ac-1` | AC bus 1 | `F_ELEC_BUS_AC_1` | 24 | ElectricalBus `ac-1` | 0.9 (BLOCK 7) | FLIPPP Reckless, fenixhangarweb component aliases |
| `electrical.bus.ac-2` | AC bus 2 | `F_ELEC_BUS_AC_2` | 24 | ElectricalBus `ac-2` | 0.12.0-preview.4 | — |
| `electrical.bus.ac-ess` | AC essential bus | `F_ELEC_BUS_AC_ESSENTIAL` | 24 | ElectricalBus `ac-ess` | 0.9 (BLOCK 7) | fenixhangarweb component aliases |
| `electrical.bus.ac-ess-shed` | AC essential shed bus | `F_ELEC_BUS_AC_SHED` | 24 | ElectricalBus `ac-ess-shed` | 0.12.0-preview.4 | — |
| `electrical.bus.ac-static-inverter` | AC static inverter bus | `F_ELEC_BUS_AC_STATIC_INVERTER` | 24 | ElectricalBus `ac-static-inverter` | 0.12.0-preview.4 | — |
| `electrical.bus.ac-1-26v` | AC bus 1, 26 V | `F_ELEC_BUS_AC26_1` | 24 | ElectricalBus `ac-1-26v` | 0.12.0-preview.4 | — |
| `electrical.bus.ac-2-26v` | AC bus 2, 26 V | `F_ELEC_BUS_AC26_2` | 24 | ElectricalBus `ac-2-26v` | 0.12.0-preview.4 | — |
| `electrical.bus.ac-ess-26v` | AC essential bus, 26 V | `F_ELEC_BUS_AC26_ESS` | 24 | ElectricalBus `ac-ess-26v` | 0.12.0-preview.4 | — |
| `electrical.generator.apu` | APU generator | `F_ELEC_APU_GEN` | 24 | APU | 0.12.0-preview.4 | — |
| `electrical.generator.1` | Generator 1 (IDG 1 drive) | `F_ELEC_DRIVE_FAILURE_L` | 24 | Aircraft | 0.9 (BLOCK 7) | FLIPPP Tricky |
| `electrical.generator.2` | Generator 2 (IDG 2 drive) | `F_ELEC_DRIVE_FAILURE_R` | 24 | Aircraft | 0.9 (BLOCK 7) | FLIPPP Tricky |
| `electrical.idg.1.oil-overheat` | IDG 1 oil overheat | `F_ELEC_GEN_OVERHEAT_L` | 24 | Aircraft | 0.12.0-preview.4 | — |
| `electrical.idg.2.oil-overheat` | IDG 2 oil overheat | `F_ELEC_GEN_OVERHEAT_R` | 24 | Aircraft | 0.12.0-preview.4 | — |
| `electrical.idg.1.oil-low-pressure` | IDG 1 oil low pressure | `F_ELEC_DRIVE_OIL_LOW_L` | 24 | Aircraft | 0.12.0-preview.4 | — |
| `electrical.idg.2.oil-low-pressure` | IDG 2 oil low pressure | `F_ELEC_DRIVE_OIL_LOW_R` | 24 | Aircraft | 0.12.0-preview.4 | — |
| `electrical.static-inverter` | Static inverter | `F_ELEC_STATIC_INVERTER` | 24 | Aircraft | 0.9 (BLOCK 7) | FLIPPP Easy |
| `electrical.transformer-rectifier.1` | Transformer rectifier 1 | `F_ELEC_TR1` | 24 | Aircraft | 0.12.0-preview.4 | — |
| `electrical.transformer-rectifier.2` | Transformer rectifier 2 | `F_ELEC_TR2` | 24 | Aircraft | 0.12.0-preview.4 | — |
| `electrical.transformer-rectifier.ess` | Essential transformer rectifier | `F_ELEC_TR_ESS` | 24 | Aircraft | 0.12.0-preview.4 | — |
| `electrical.ac-ess-feed.alternate` | AC essential bus alternate feed | `F_ELEC_AC_ESS_ALTN` | 24 | Aircraft | 0.12.0-preview.4 | — |
| `electrical.ac-ess-feed.ac-1` | AC essential feed from AC bus 1 | `F_ELEC_AC_ESS_FEED_1` | 24 | Aircraft | 0.12.0-preview.4 | — |
| `electrical.ac-ess-feed.ac-2` | AC essential feed from AC bus 2 | `F_ELEC_AC_ESS_FEED_2` | 24 | Aircraft | 0.12.0-preview.4 | — |
| `fire.sdcu` | Smoke detection control unit | `F_SDCU` | 26 | Aircraft | 0.12.0-preview.4 | — |
| `fire.cargo.forward.smoke` | Forward cargo smoke detected | `F_FIRE_CARGO_FWD_SMOKE` | 26 | Aircraft | 0.12.0-preview.4 | — |
| `fire.cargo.aft.smoke` | Aft cargo smoke detected | `F_FIRE_CARGO_AFT_SMOKE` | 26 | Aircraft | 0.12.0-preview.4 | — |
| `fire.lavatory.smoke` | Lavatory smoke | `F_FIRE_LAVATORY_SMOKE` | 26 | Aircraft | 0.9 (BLOCK 7) | FLIPPP Tricky |
| `fire.engine.1.unextinguishable` | Engine 1 unextinguishable fire | `F_OH_FIRE_ENG_1` | 26 | Engine 1 | 0.12.0-preview.4 | — |
| `fire.engine.2.unextinguishable` | Engine 2 unextinguishable fire | `F_OH_FIRE_ENG_2` | 26 | Engine 2 | 0.12.0-preview.4 | — |
| `fire.apu.unextinguishable` | APU unextinguishable fire | `F_OH_FIRE_APU` | 26 | APU | 0.12.0-preview.4 | — |
| `fire.engine.1.extinguished-one-bottle` | Engine 1 fire, extinguished with one bottle | `F_OH_FIRE_ENG_1_1BOTTLE` | 26 | Engine 1 | 0.12.0-preview.4 | — |
| `fire.engine.2.extinguished-one-bottle` | Engine 2 fire, extinguished with one bottle | `F_OH_FIRE_ENG_2_1BOTTLE` | 26 | Engine 2 | 0.12.0-preview.4 | — |
| `fire.apu.extinguished-one-bottle` | APU fire, extinguished with one bottle | `F_OH_FIRE_APU_1BOTTLE` | 26 | APU | 0.12.0-preview.4 | — |
| `fire.engine.1.extinguished-two-bottles` | Engine 1 fire, extinguished with two bottles | `F_OH_FIRE_ENG_1_2BOTTLES` | 26 | Engine 1 | 0.12.0-preview.4 | — |
| `fire.engine.2.extinguished-two-bottles` | Engine 2 fire, extinguished with two bottles | `F_OH_FIRE_ENG_2_2BOTTLES` | 26 | Engine 2 | 0.12.0-preview.4 | — |
| `fire.engine.1.loop-a` | Engine 1 fire detection loop A | `F_OH_FIRE_ENG1_LOOP_A` | 26 | Engine 1 | 0.9 (BLOCK 7) | FLIPPP Reckless, FSHANGAR fire-probe trials |
| `fire.engine.1.loop-b` | Engine 1 fire detection loop B | `F_OH_FIRE_ENG1_LOOP_B` | 26 | Engine 1 | 0.12.0-preview.4 | — |
| `fire.engine.2.loop-a` | Engine 2 fire detection loop A | `F_OH_FIRE_ENG2_LOOP_A` | 26 | Engine 2 | 0.12.0-preview.4 | — |
| `fire.engine.2.loop-b` | Engine 2 fire detection loop B | `F_OH_FIRE_ENG2_LOOP_B` | 26 | Engine 2 | 0.12.0-preview.4 | — |
| `fire.apu.loop-a` | APU fire detection loop A | `F_OH_FIRE_AP2_LOOP_A` | 26 | APU | 0.12.0-preview.4 | — |
| `fire.apu.loop-b` | APU fire detection loop B | `F_OH_FIRE_APU_LOOP_B` | 26 | APU | 0.12.0-preview.4 | — |
| `fire.fdu.1` | Fire detection unit 1 | `F_FIRE_FDU1` | 26 | Aircraft | 0.9 (BLOCK 7) | FSHANGAR fire-probe experiment (paused, never injected) |
| `fire.fdu.2` | Fire detection unit 2 | `F_FIRE_FDU2` | 26 | Aircraft | 0.12.0-preview.4 | — |
| `fire.fdu.3` | Fire detection unit 3 | `F_FIRE_FDU3` | 26 | Aircraft | 0.12.0-preview.4 | — |
| `fire.avionics.smoke` | Avionics smoke detected | `F_FIRE_AVIONICS_SMOKE` | 26 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.fqi.channel-1` | Fuel quantity indication channel 1 | `F_FUEL_FQI1` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.fqi.channel-2` | Fuel quantity indication channel 2 | `F_FUEL_FQI2` | 28 | Aircraft | 0.9 (BLOCK 7) | FLIPPP Easy |
| `fuel.pump.left-1` | Left tank fuel pump 1 | `F_FUEL_PUMP_LEFT_1` | 28 | FuelPump `left-1` | 0.9 (BLOCK 7) | fenixhangarweb component aliases |
| `fuel.pump.left-2` | Left tank fuel pump 2 | `F_FUEL_PUMP_LEFT_2` | 28 | FuelPump `left-2` | 0.12.0-preview.4 | — |
| `fuel.pump.center-1` | Center tank fuel pump 1 | `F_FUEL_PUMP_CENTER_1` | 28 | FuelPump `center-1` | 0.12.0-preview.4 | — |
| `fuel.pump.center-2` | Center tank fuel pump 2 | `F_FUEL_PUMP_CENTER_2` | 28 | FuelPump `center-2` | 0.12.0-preview.4 | — |
| `fuel.pump.right-1` | Right tank fuel pump 1 | `F_FUEL_PUMP_RIGHT_1` | 28 | FuelPump `right-1` | 0.9 (BLOCK 7) | fenixhangarweb component aliases |
| `fuel.pump.right-2` | Right tank fuel pump 2 | `F_FUEL_PUMP_RIGHT_2` | 28 | FuelPump `right-2` | 0.12.0-preview.4 | — |
| `fuel.inner-tank.high-temperature.ecam` | Inner tank high fuel temperature (ECAM) | `F_FUEL_HIGH_TEMP_INNER` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.outer-tank.high-temperature.ecam` | Outer tank high fuel temperature (ECAM) | `F_FUEL_HIGH_TEMP_OUTER` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.inner-tank.high-temperature.advisory` | Inner tank high fuel temperature (advisory) | `F_FUEL_HIGH_TEMP_INNER_ADV` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.outer-tank.high-temperature.advisory` | Outer tank high fuel temperature (advisory) | `F_FUEL_HIGH_TEMP_OUTER_ADV` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.inner-tank.low-temperature.ecam` | Inner tank low fuel temperature (ECAM) | `F_FUEL_LOW_TEMP_INNER` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.outer-tank.low-temperature.ecam` | Outer tank low fuel temperature (ECAM) | `F_FUEL_LOW_TEMP_OUTER` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.inner-tank.low-temperature.advisory` | Inner tank low fuel temperature (advisory) | `F_FUEL_LOW_TEMP_INNER_ADV` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.outer-tank.low-temperature.advisory` | Outer tank low fuel temperature (advisory) | `F_FUEL_LOW_TEMP_OUTER_ADV` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.transfer-valve.left-1` | Left transfer valve 1 | `F_FUEL_XFER_VALVE_1_L` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.transfer-valve.left-2` | Left transfer valve 2 | `F_FUEL_XFER_VALVE_2_L` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.transfer-valve.right-1` | Right transfer valve 1 | `F_FUEL_XFER_VALVE_1_R` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.transfer-valve.right-2` | Right transfer valve 2 | `F_FUEL_XFER_VALVE_2_R` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.engine-valve.1.stuck` | Engine 1 fuel valve stuck | `F_FUEL_ENGINE_1_VALVE` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.engine-valve.2.stuck` | Engine 2 fuel valve stuck | `F_FUEL_ENGINE_2_VALVE` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.crossfeed-valve` | Crossfeed valve | `F_FUEL_VALVE_CROSSFEED` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.defuel-transfer-valve` | Defuel/transfer valve | `F_DEFUEL_TRANSFER_VALVE` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.leak.left-outer` | Fuel leak, left outer tank | `F_FUEL_LEAK_LEFT_OUTER` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.leak.left-inner` | Fuel leak, left inner tank | `F_FUEL_LEAK_LEFT_INNER` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.leak.center` | Fuel leak, center tank | `F_FUEL_LEAK_CENTER` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.leak.right-inner` | Fuel leak, right inner tank | `F_FUEL_LEAK_RIGHT_INNER` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.leak.right-outer` | Fuel leak, right outer tank | `F_FUEL_LEAK_RIGHT_OUTER` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.hp-valve.1` | Engine 1 HP fuel valve | `F_ENG1_HP_VALVE` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `fuel.hp-valve.2` | Engine 2 HP fuel valve | `F_ENG2_HP_VALVE` | 28 | Aircraft | 0.12.0-preview.4 | — |
| `hydraulic.green.leak` | Green hydraulic leak | `F_HYD_LEAK_GREEN` | 29 | HydraulicSystem `green` | 0.9 (BLOCK 7) | fenixhangarweb component aliases |
| `hydraulic.blue.leak` | Blue hydraulic leak | `F_HYD_LEAK_BLUE` | 29 | HydraulicSystem `blue` | 0.9 (BLOCK 7) | FLIPPP Reckless |
| `hydraulic.yellow.leak` | Yellow hydraulic leak | `F_HYD_LEAK_YELLOW` | 29 | HydraulicSystem `yellow` | 0.12.0-preview.4 | — |
| `hydraulic.green.reservoir-overheat` | Green reservoir overheat | `F_HYD_RSVR_OVERHEAT_GREEN` | 29 | HydraulicSystem `green` | 0.12.0-preview.4 | — |
| `hydraulic.blue.reservoir-overheat` | Blue reservoir overheat | `F_HYD_RSVR_OVERHEAT_BLUE` | 29 | HydraulicSystem `blue` | 0.12.0-preview.4 | — |
| `hydraulic.yellow.reservoir-overheat` | Yellow reservoir overheat | `F_HYD_RSVR_OVERHEAT_YELLOW` | 29 | HydraulicSystem `yellow` | 0.12.0-preview.4 | — |
| `hydraulic.green.reservoir-low-air-pressure` | Green reservoir low air pressure | `F_HYD_RSVR_AIR_PRESSURE_GREEN` | 29 | HydraulicSystem `green` | 0.12.0-preview.4 | — |
| `hydraulic.blue.reservoir-low-air-pressure` | Blue reservoir low air pressure | `F_HYD_RSVR_AIR_PRESSURE_BLUE` | 29 | HydraulicSystem `blue` | 0.12.0-preview.4 | — |
| `hydraulic.yellow.reservoir-low-air-pressure` | Yellow reservoir low air pressure | `F_HYD_RSVR_AIR_PRESSURE_YELLOW` | 29 | HydraulicSystem `yellow` | 0.12.0-preview.4 | — |
| `hydraulic.green.low-level` | Green hydraulic reservoir low level | `F_HYD_LOW_GREEN` | 29 | HydraulicSystem `green` | 0.9 (BLOCK 7) | fenixhangarweb component aliases |
| `hydraulic.blue.low-level` | Blue hydraulic reservoir low level | `F_HYD_LOW_BLUE` | 29 | HydraulicSystem `blue` | 0.9 (BLOCK 7) | FLIPPP Tricky |
| `hydraulic.yellow.low-level` | Yellow hydraulic reservoir low level | `F_HYD_LOW_YELLOW` | 29 | HydraulicSystem `yellow` | 0.12.0-preview.4 | — |
| `hydraulic.engine-pump.1` | Engine 1 hydraulic pump | `F_HYD_PUMP_ENG_1` | 29 | Aircraft | 0.12.0-preview.4 | — |
| `hydraulic.engine-pump.2` | Engine 2 hydraulic pump | `F_HYD_PUMP_ENG_2` | 29 | Aircraft | 0.12.0-preview.4 | — |
| `hydraulic.fire-valve.1` | Engine 1 hydraulic fire valve | `F_HYD_FIRE_VALVE_1` | 29 | Aircraft | 0.12.0-preview.4 | — |
| `hydraulic.fire-valve.2` | Engine 2 hydraulic fire valve | `F_HYD_FIRE_VALVE_2` | 29 | Aircraft | 0.12.0-preview.4 | — |
| `hydraulic.ptu` | Power transfer unit fault | `F_HYD_PTU` | 29 | Aircraft | 0.12.0-preview.4 | — |
| `hydraulic.blue.electric-pump` | Blue electric hydraulic pump | `F_HYD_PUMP_BLUE` | 29 | HydraulicSystem `blue` | 0.9 (BLOCK 7) | FLIPPP Tricky |
| `hydraulic.yellow.electric-pump` | Yellow electric hydraulic pump | `F_HYD_PUMP_YELLOW` | 29 | HydraulicSystem `yellow` | 0.9 (BLOCK 7) | FLIPPP Tricky |
| `hydraulic.blue.electric-pump.overheat` | Blue electric hydraulic pump overheat | `F_HYD_PUMP_OVERHEAT_BLUE` | 29 | HydraulicSystem `blue` | 0.12.0-preview.4 | — |
| `hydraulic.yellow.electric-pump.overheat` | Yellow electric hydraulic pump overheat | `F_HYD_PUMP_OVERHEAT_YELLOW` | 29 | HydraulicSystem `yellow` | 0.12.0-preview.4 | — |
| `landing-gear.bscu.1` | BSCU system 1 | `F_BSCU_1` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `landing-gear.bscu.2` | BSCU system 2 | `F_BSCU_2` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `landing-gear.abcu` | Alternate braking control unit | `F_BRAKE_ABCU` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `landing-gear.bscu.1.brake-fault` | BSCU 1 brake fault | `F_HYD_BSCU_1` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `landing-gear.bscu.2.brake-fault` | BSCU 2 brake fault | `F_HYD_BSCU_2` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `landing-gear.autobrake` | Autobrake failure | `F_BRAKE_AUTOBRAKE` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `landing-gear.brake.wheel-1` | Wheel 1 brake fault | `F_BRAKE_WHEEL_1` | 32 | Aircraft | 0.9 (BLOCK 7) | FLIPPP Tricky |
| `landing-gear.brake.wheel-2` | Wheel 2 brake fault | `F_BRAKE_WHEEL_2` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `landing-gear.brake.wheel-3` | Wheel 3 brake fault | `F_BRAKE_WHEEL_3` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `landing-gear.brake.wheel-4` | Wheel 4 brake fault | `F_BRAKE_WHEEL_4` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `landing-gear.tyre-pressure.main-1` | Main tyre 1 low pressure | `F_GEAR_TYRE_PSI_MAIN_1` | 32 | Aircraft | 0.9 (BLOCK 7) | FLIPPP Reckless |
| `landing-gear.tyre-pressure.main-2` | Main tyre 2 low pressure | `F_GEAR_TYRE_PSI_MAIN_2` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `landing-gear.tyre-pressure.left-1` | Left tyre 1 low pressure | `F_GEAR_TYRE_PSI_LEFT_1` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `landing-gear.tyre-pressure.left-2` | Left tyre 2 low pressure | `F_GEAR_TYRE_PSI_LEFT_2` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `landing-gear.tyre-pressure.right-1` | Right tyre 1 low pressure | `F_GEAR_TYRE_PSI_RIGHT_1` | 32 | Aircraft | 0.9 (BLOCK 7) | FSHANGAR live verification and tests |
| `landing-gear.tyre-pressure.right-2` | Right tyre 2 low pressure | `F_GEAR_TYRE_PSI_RIGHT_2` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `landing-gear.nose-wheel-steering` | Nose wheel steering | `F_BRAKE_NWS` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `landing-gear.lgciu.1` | LGCIU 1 | `F_MISC_LGCIU1` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `landing-gear.lgciu.2` | LGCIU 2 | `F_MISC_LGCIU2` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `landing-gear.safety-valve` | Gear safety valve | `F_GEAR_SAFETY_VALVE` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `landing-gear.lock.left-main` | Left main gear does not lock | `F_GEAR_LOCK_LEFT` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `landing-gear.lock.nose` | Nose gear does not lock | `F_GEAR_LOCK_NOSE` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `landing-gear.lock.right-main` | Right main gear does not lock | `F_GEAR_LOCK_RIGHT` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `landing-gear.gear.locked-up` | Gear locked up | `F_GEAR_LOCKED_UP` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `landing-gear.gear.locked-down` | Gear locked down | `F_GEAR_LOCKED_DOWN` | 32 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.ir.1.failure` | IR 1 failure | `F_OH_NAV_IR_TOTAL_1` | 34 | InertialReference 1 | 0.12.0-preview.4 | — |
| `navigation.ir.2.failure` | IR 2 failure | `F_OH_NAV_IR_TOTAL_2` | 34 | InertialReference 2 | 0.12.0-preview.4 | — |
| `navigation.ir.3.failure` | IR 3 failure | `F_OH_NAV_IR_TOTAL_3` | 34 | InertialReference 3 | 0.12.0-preview.4 | — |
| `navigation.ir.1.position-failure` | IR 1 position failure | `F_OH_NAV_IR_POSITION_1` | 34 | InertialReference 1 | 0.12.0-preview.4 | — |
| `navigation.ir.2.position-failure` | IR 2 position failure | `F_OH_NAV_IR_POSITION_2` | 34 | InertialReference 2 | 0.12.0-preview.4 | — |
| `navigation.ir.3.position-failure` | IR 3 position failure | `F_OH_NAV_IR_POSITION_3` | 34 | InertialReference 3 | 0.12.0-preview.4 | — |
| `navigation.adr.1` | ADR 1 failure | `F_OH_NAV_ADR_ADR1` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.adr.2` | ADR 2 failure | `F_OH_NAV_ADR_ADR2` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.adr.3` | ADR 3 failure | `F_OH_NAV_ADR_ADR3` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.ir.1.alignment` | IR 1 alignment fault | `F_OH_NAV_IR_ALIGNMENT_1` | 34 | InertialReference 1 | 0.12.0-preview.4 | — |
| `navigation.ir.2.alignment` | IR 2 alignment fault | `F_OH_NAV_IR_ALIGNMENT_2` | 34 | InertialReference 2 | 0.12.0-preview.4 | — |
| `navigation.ir.3.alignment` | IR 3 alignment fault | `F_OH_NAV_IR_ALIGNMENT_3` | 34 | InertialReference 3 | 0.12.0-preview.4 | — |
| `navigation.pitot.captain.blocked` | Captain pitot blocked | `F_OH_NAV_PITOT_BLOCKED_1` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.pitot.fo.blocked` | First officer pitot blocked | `F_OH_NAV_PITOT_BLOCKED_2` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.pitot.standby.blocked` | Standby pitot blocked | `F_OH_NAV_PITOT_BLOCKED_3` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.ir.1.pitch-discrepancy` | IR 1 pitch discrepancy | `F_NAV_IR1_PITCH_DISCREPANCY` | 34 | InertialReference 1 | 0.12.0-preview.4 | — |
| `navigation.ir.2.pitch-discrepancy` | IR 2 pitch discrepancy | `F_NAV_IR2_PITCH_DISCREPANCY` | 34 | InertialReference 2 | 0.12.0-preview.4 | — |
| `navigation.ir.3.pitch-discrepancy` | IR 3 pitch discrepancy | `F_NAV_IR3_PITCH_DISCREPANCY` | 34 | InertialReference 3 | 0.12.0-preview.4 | — |
| `navigation.ir.1.bank-discrepancy` | IR 1 bank discrepancy | `F_NAV_IR1_BANK_DISCREPANCY` | 34 | InertialReference 1 | 0.12.0-preview.4 | — |
| `navigation.ir.2.bank-discrepancy` | IR 2 bank discrepancy | `F_NAV_IR2_BANK_DISCREPANCY` | 34 | InertialReference 2 | 0.12.0-preview.4 | — |
| `navigation.ir.3.bank-discrepancy` | IR 3 bank discrepancy | `F_NAV_IR3_BANK_DISCREPANCY` | 34 | InertialReference 3 | 0.12.0-preview.4 | — |
| `navigation.ir.1.heading-discrepancy` | IR 1 heading discrepancy | `F_NAV_IR1_HDG_DISCREPANCY` | 34 | InertialReference 1 | 0.12.0-preview.4 | — |
| `navigation.ir.2.heading-discrepancy` | IR 2 heading discrepancy | `F_NAV_IR2_HDG_DISCREPANCY` | 34 | InertialReference 2 | 0.12.0-preview.4 | — |
| `navigation.ir.3.heading-discrepancy` | IR 3 heading discrepancy | `F_NAV_IR3_HDG_DISCREPANCY` | 34 | InertialReference 3 | 0.12.0-preview.4 | — |
| `navigation.ir.1.disagree` | IR 1 disagree | `F_NAV_IR_DISAGREE1` | 34 | InertialReference 1 | 0.12.0-preview.4 | — |
| `navigation.ir.2.disagree` | IR 2 disagree | `F_NAV_IR_DISAGREE2` | 34 | InertialReference 2 | 0.12.0-preview.4 | — |
| `navigation.mcdu.1` | MCDU 1 | `F_MCDU_1` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.mcdu.2` | MCDU 2 | `F_MCDU_2` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.mcdu.1.recoverable-fault` | MCDU 1 recoverable fault | `F_MCDU_1_RECOVERABLE` | 34 | Aircraft | 0.9 (BLOCK 7) | FLIPPP Easy |
| `navigation.mcdu.2.recoverable-fault` | MCDU 2 recoverable fault | `F_MCDU_2_RECOVERABLE` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.fmgc.1` | FMGC 1 | `F_FMGC_1` | 34 | Aircraft | 0.9 (BLOCK 7) | FLIPPP Easy |
| `navigation.fmgc.2` | FMGC 2 | `F_FMGC_2` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.gpwc` | Ground proximity warning computer | `F_GPWS` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.vor.1` | VOR 1 | `F_NAV_VOR1` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.vor.2` | VOR 2 | `F_NAV_VOR2` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.adf.1` | ADF 1 | `F_NAV_ADF1` | 34 | Aircraft | 0.9 (BLOCK 7) | FLIPPP Easy |
| `navigation.adf.2` | ADF 2 | `F_NAV_ADF2` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.ils.1.localizer` | ILS 1 localizer | `F_NAV_ILS1_LOC` | 34 | Aircraft | 0.9 (BLOCK 7) | FLIPPP Tricky |
| `navigation.ils.1.glideslope` | ILS 1 glideslope | `F_NAV_ILS1_GS` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.ils.2.localizer` | ILS 2 localizer | `F_NAV_ILS2_LOC` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.ils.2.glideslope` | ILS 2 glideslope | `F_NAV_ILS2_GS` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.ils-transmitter.localizer` | Localizer transmitter | `F_NAV_LOC` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.ils-transmitter.glideslope` | Glideslope transmitter | `F_NAV_GS` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.gps.1` | GPS 1 | `F_NAV_GPS1` | 34 | Aircraft | 0.9 (BLOCK 7) | FLIPPP Easy |
| `navigation.gps.2` | GPS 2 | `F_NAV_GPS2` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.radio-altimeter.1` | Radio altimeter 1 | `F_NAV_RALT1` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.radio-altimeter.2` | Radio altimeter 2 | `F_NAV_RALT2` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.accuracy-downgrade` | Navigation accuracy downgrade | `F_NAV_ACCURACY_DOWNGRADE` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.tcas` | TCAS | `F_NAV_TCAS` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.atc.1` | ATC transponder 1 | `F_NAV_ATC1` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `navigation.atc.2` | ATC transponder 2 | `F_NAV_ATC2` | 34 | Aircraft | 0.12.0-preview.4 | — |
| `pneumatic.lavatory-galley-fan` | Lavatory/galley fan | `F_PNEUMATIC_LAV_GAL_FAN` | 36 | Aircraft | 0.12.0-preview.4 | — |
| `pneumatic.hp-valve.1` | Engine 1 HP bleed valve | `F_PNEUMATIC_HP_VALVE_1` | 36 | Aircraft | 0.12.0-preview.4 | — |
| `pneumatic.hp-valve.2` | Engine 2 HP bleed valve | `F_PNEUMATIC_HP_VALVE_2` | 36 | Aircraft | 0.12.0-preview.4 | — |
| `pneumatic.ram-air-valve` | Ram air valve | `F_PNEUMATIC_RAM_AIR_VALVE` | 36 | Aircraft | 0.12.0-preview.4 | — |
| `pneumatic.bleed-valve.1` | Engine 1 bleed valve | `F_PNEUMATIC_BLEED_VALVE_1` | 36 | Aircraft | 0.9 (BLOCK 7) | FSHANGAR live verification and tests, fenixhangarweb API docs example |
| `pneumatic.bleed-valve.2` | Engine 2 bleed valve | `F_PNEUMATIC_BLEED_VALVE_2` | 36 | Aircraft | 0.12.0-preview.4 | — |
| `pneumatic.hot-air-valve` | Hot air valve | `F_PNEUMATIC_HOT_AIR_VALVE` | 36 | Aircraft | 0.12.0-preview.4 | — |
| `pneumatic.bleed.1.low-temperature` | Engine 1 bleed low temperature | `F_ENG1_BLEED_LOW_TEMP` | 36 | Aircraft | 0.12.0-preview.4 | — |
| `pneumatic.bleed.2.low-temperature` | Engine 2 bleed low temperature | `F_ENG2_BLEED_LOW_TEMP` | 36 | Aircraft | 0.12.0-preview.4 | — |
| `pneumatic.bmc.1` | Bleed monitoring computer 1 | `F_PNEUMATIC_BMC_1` | 36 | Aircraft | 0.12.0-preview.4 | — |
| `pneumatic.bmc.2` | Bleed monitoring computer 2 | `F_PNEUMATIC_BMC_2` | 36 | Aircraft | 0.12.0-preview.4 | — |
| `pneumatic.crossbleed-valve` | Crossbleed valve | `F_PNEUMATIC_CROSS_BLEED_VALVE` | 36 | Aircraft | 0.12.0-preview.4 | — |
| `pneumatic.apu-bleed-valve` | APU bleed valve | `F_PNEUMATIC_APU_VALVE` | 36 | Aircraft | 0.12.0-preview.4 | — |
| `pneumatic.pack-valve.1` | Pack 1 flow control valve | `F_PNEUMATIC_PACK_VALVE_1` | 36 | Aircraft | 0.12.0-preview.4 | — |
| `pneumatic.pack-valve.2` | Pack 2 flow control valve | `F_PNEUMATIC_PACK_VALVE_2` | 36 | Aircraft | 0.12.0-preview.4 | — |
| `pneumatic.leak.wing.1` | Bleed leak, engine 1 wing | `F_PNEUMATIC_LEAK_WING_ENG1` | 36 | Aircraft | 0.12.0-preview.4 | — |
| `pneumatic.leak.wing.2` | Bleed leak, engine 2 wing | `F_PNEUMATIC_LEAK_WING_ENG2` | 36 | Aircraft | 0.12.0-preview.4 | — |
| `pneumatic.leak.pylon.1` | Bleed leak, engine 1 pylon | `F_PNEUMATIC_LEAK_PYLON_ENG1` | 36 | Aircraft | 0.12.0-preview.4 | — |
| `pneumatic.leak.pylon.2` | Bleed leak, engine 2 pylon | `F_PNEUMATIC_LEAK_PYLON_ENG2` | 36 | Aircraft | 0.12.0-preview.4 | — |
| `pneumatic.leak.apu` | Bleed leak, APU | `F_PNEUMATIC_LEAK_APU` | 36 | Aircraft | 0.12.0-preview.4 | — |
| `apu.ecb` | APU electronic control box | `F_ELEC_APU_ECB` | 49 | APU | 0.12.0-preview.4 | — |
| `apu.oil.low-level` | APU low oil level | `F_APU_LOW_OIL` | 49 | APU | 0.12.0-preview.4 | — |
| `apu.fuel-valve` | APU fuel valve | `F_FUEL_VALVE_APU` | 49 | APU | 0.12.0-preview.4 | — |
| `communications.stuck-mic.captain` | Captain stuck microphone | `F_COMM_STUCK_PTT_CAPT` | 23 | Aircraft | 0.12.0-preview.4 | — |
| `communications.stuck-mic.fo` | First officer stuck microphone | `F_COMM_STUCK_PTT_FO` | 23 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.wing-anti-ice-valve.left` | Left wing anti-ice valve | `F_PNEUMATIC_WAI_1` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.wing-anti-ice-valve.right` | Right wing anti-ice valve | `F_PNEUMATIC_WAI_2` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.engine-anti-ice-valve.1` | Engine 1 anti-ice valve | `F_PNEUMATIC_EAI_1` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.engine-anti-ice-valve.2` | Engine 2 anti-ice valve | `F_PNEUMATIC_EAI_2` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.whc.1` | Window heat computer 1 | `F_ICE_WHC_1` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.whc.2` | Window heat computer 2 | `F_ICE_WHC_2` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.aoa-heat.captain` | Captain AOA probe heat | `F_ICE_AOA_HEAT_CPT` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.aoa-heat.fo` | First officer AOA probe heat | `F_ICE_AOA_FO` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.aoa-heat.standby` | Standby AOA probe heat | `F_ICE_AOA_STBY` | 30 | Aircraft | 0.9 (BLOCK 7) | FLIPPP Easy |
| `ice-rain.tat-heat.captain` | Captain TAT probe heat | `F_ICE_TAT_HEAT_CPT` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.tat-heat.fo` | First officer TAT probe heat | `F_ICE_TAT_FO` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.tat-heat.standby` | Standby TAT probe heat | `F_ICE_TAT_STBY` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.pitot-heat.captain` | Captain pitot heat | `F_ICE_PITOT_HEAT_CPT` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.pitot-heat.fo` | First officer pitot heat | `F_ICE_PITOT_FO` | 30 | Aircraft | 0.9 (BLOCK 7) | FLIPPP Tricky |
| `ice-rain.pitot-heat.standby` | Standby pitot heat | `F_ICE_PITOT_STBY` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.static-heat.captain.left` | Captain left static port heat | `F_ICE_STAT_HEAT_CPT_L` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.static-heat.captain.right` | Captain right static port heat | `F_ICE_STAT_HEAT_CPT_R` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.static-heat.fo.left` | First officer left static port heat | `F_ICE_STAT_FO_L` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.static-heat.fo.right` | First officer right static port heat | `F_ICE_STAT_FO_R` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.static-heat.standby.left` | Standby left static port heat | `F_ICE_STAT_STBY_L` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.static-heat.standby.right` | Standby right static port heat | `F_ICE_STAT_STBY_R` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.phc.1` | Probe heat computer 1 | `F_ICE_PHC1` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.phc.2` | Probe heat computer 2 | `F_ICE_PHC2` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.phc.3` | Probe heat computer 3 | `F_ICE_PHC3` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.engine-icing.1` | Engine 1 icing | `F_ICING_ENG1` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `ice-rain.engine-icing.2` | Engine 2 icing | `F_ICING_ENG2` | 30 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.ippu.1` | IPPU 1 | `F_FC_IPPU1` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.ippu.2` | IPPU 2 | `F_FC_IPPU2` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.flap.locked` | Flaps locked | `F_HYD_WTB_FLAP` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.slat.locked` | Slats locked | `F_HYD_WTB_SLAT` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.flaps.alignment-fault` | Flap alignment fault | `F_HYD_FSCC_ALIGNMENT` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.sfcc.1` | SFCC 1 | `F_OH_FLT_CLT_SFCC_1` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.sfcc.2` | SFCC 2 | `F_OH_FLT_CLT_SFCC_2` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.sfcc.1.flap-channel` | SFCC 1 flap channel | `F_SFCC_1_FLAP` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.sfcc.2.flap-channel` | SFCC 2 flap channel | `F_SFCC_2_FLAP` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.sfcc.1.slat-channel` | SFCC 1 slat channel | `F_SFCC_1_SLAT` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.sfcc.2.slat-channel` | SFCC 2 slat channel | `F_SFCC_2_SLAT` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.elac.1` | ELAC 1 | `F_OH_FLT_CTL_ELAC_1` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.elac.2` | ELAC 2 | `F_OH_FLT_CTL_ELAC_2` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.sec.1` | SEC 1 | `F_OH_FLT_CTL_SEC_1` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.sec.2` | SEC 2 | `F_OH_FLT_CTL_SEC_2` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.sec.3` | SEC 3 | `F_OH_FLT_CTL_SEC_3` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.elac.1.resettable-fault` | ELAC 1 resettable fault | `F_OH_FLT_CTL_ELAC_RECOVERABLE_1` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.elac.2.resettable-fault` | ELAC 2 resettable fault | `F_OH_FLT_CTL_ELAC_RECOVERABLE_2` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.sec.1.resettable-fault` | SEC 1 resettable fault | `F_OH_FLT_CTL_SEC_RECOVERABLE_1` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.sec.2.resettable-fault` | SEC 2 resettable fault | `F_OH_FLT_CTL_SEC_RECOVERABLE_2` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.sec.3.resettable-fault` | SEC 3 resettable fault | `F_OH_FLT_CTL_SEC_RECOVERABLE_3` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.yaw-damper.1` | Yaw damper channel 1 | `F_FC_YAW_DAMPER_1` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.yaw-damper.2` | Yaw damper channel 2 | `F_FC_YAW_DAMPER_2` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.sidestick.captain.pitch-reversal` | Captain sidestick pitch reversal | `F_FC_SIDESTICK_REV_PITCH_CAPT` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.sidestick.captain.roll-reversal` | Captain sidestick roll reversal | `F_FC_SIDESTICK_REV_ROLL_CAPT` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.sidestick.fo.pitch-reversal` | First officer sidestick pitch reversal | `F_FC_SIDESTICK_REV_PITCH_FO` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.sidestick.fo.roll-reversal` | First officer sidestick roll reversal | `F_FC_SIDESTICK_REV_ROLL_FO` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.sidestick.captain.fault` | Captain sidestick fault | `F_FC_SIDESTICK_FAULT_CAPT` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.sidestick.fo.fault` | First officer sidestick fault | `F_FC_SIDESTICK_FAULT_FO` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.fcdc.1` | FCDC 1 | `B_INT_SFCDC1F` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.fcdc.2` | FCDC 2 | `B_INT_SFCDC2F` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.stabilizer.jam` | Stabilizer jam | `F_FCTL_STAB_JAM` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.elevator.both` | Left and right elevator failure | `F_FCTL_ELEV_LR` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.elevator.left` | Left elevator failure | `F_FCTL_ELEV_L` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `flight-controls.elevator.right` | Right elevator failure | `F_FCTL_ELEV_R` | 27 | Aircraft | 0.12.0-preview.4 | — |
| `engine.1.start-valve` | Engine 1 start valve | `F_PNEUMATIC_START_VALVE_1` | 80 | Engine 1 | 0.12.0-preview.4 | — |
| `engine.2.start-valve` | Engine 2 start valve | `F_PNEUMATIC_START_VALVE_2` | 80 | Engine 2 | 0.12.0-preview.4 | — |
| `engine.1.start.hot` | Engine 1 hot start | `F_START_FAULT_1_HOT_START` | 80 | Engine 1 | 0.12.0-preview.4 | — |
| `engine.2.start.hot` | Engine 2 hot start | `F_START_FAULT_2_HOT_START` | 80 | Engine 2 | 0.12.0-preview.4 | — |
| `engine.1.start.hung` | Engine 1 hung start | `F_START_FAULT_1_HUNG_START` | 80 | Engine 1 | 0.12.0-preview.4 | — |
| `engine.2.start.hung` | Engine 2 hung start | `F_START_FAULT_2_HUNG_START` | 80 | Engine 2 | 0.12.0-preview.4 | — |
| `engine.1.start.no-ignition` | Engine 1 no ignition | `F_START_FAULT_1_NO_IGNITION` | 80 | Engine 1 | 0.12.0-preview.4 | — |
| `engine.2.start.no-ignition` | Engine 2 no ignition | `F_START_FAULT_2_NO_IGNITION` | 80 | Engine 2 | 0.12.0-preview.4 | — |
| `indicating.cfdiu` | CFDIU | `F_CFDIU` | 31 | Aircraft | 0.12.0-preview.4 | — |
| `indicating.ecp` | ECAM control panel | `F_ECP` | 31 | Aircraft | 0.12.0-preview.4 | — |
| `indicating.sdac.1` | SDAC 1 | `F_SDAC_1` | 31 | Aircraft | 0.12.0-preview.4 | — |
| `indicating.sdac.2` | SDAC 2 | `F_SDAC_2` | 31 | Aircraft | 0.12.0-preview.4 | — |
| `indicating.fwc.1` | Flight warning computer 1 | `F_FWC1` | 31 | Aircraft | 0.12.0-preview.4 | — |
| `indicating.fwc.2` | Flight warning computer 2 | `F_FWC2` | 31 | Aircraft | 0.12.0-preview.4 | — |
| `indicating.cvr` | Cockpit voice recorder | `F_CVR` | 31 | Aircraft | 0.12.0-preview.4 | — |
| `indicating.dmc.1` | Display management computer 1 | `F_DISPLAY_DMC_1` | 31 | Aircraft | 0.12.0-preview.4 | — |
| `indicating.dmc.2` | Display management computer 2 | `F_DISPLAY_DMC_2` | 31 | Aircraft | 0.12.0-preview.4 | — |
| `indicating.dmc.3` | Display management computer 3 | `F_DISPLAY_DMC_3` | 31 | Aircraft | 0.12.0-preview.4 | — |
| `indicating.dcdu.captain` | Captain DCDU | `F_DISPLAY_DCDU_CAPT` | 31 | Aircraft | 0.12.0-preview.4 | — |
| `indicating.dcdu.fo` | First officer DCDU | `F_DISPLAY_DCDU_FO` | 31 | Aircraft | 0.12.0-preview.4 | — |
| `indicating.display.captain-pfd` | Captain PFD display unit | `F_DISPLAY_DU_CAPTAIN_OUT` | 31 | Aircraft | 0.12.0-preview.4 | — |
| `indicating.display.captain-nd` | Captain ND display unit | `F_DISPLAY_DU_CAPTAIN_IN` | 31 | Aircraft | 0.12.0-preview.4 | — |
| `indicating.display.fo-nd` | First officer ND display unit | `F_DISPLAY_DU_FO_IN` | 31 | Aircraft | 0.12.0-preview.4 | — |
| `indicating.display.fo-pfd` | First officer PFD display unit | `F_DISPLAY_DU_FO_OUT` | 31 | Aircraft | 0.12.0-preview.4 | — |
| `indicating.display.ecam-upper` | Upper ECAM display unit | `F_DISPLAY_DU_ECAM_UPPER` | 31 | Aircraft | 0.12.0-preview.4 | — |
| `indicating.display.ecam-lower` | Lower ECAM display unit | `F_DISPLAY_DU_ECAM_LOWER` | 31 | Aircraft | 0.9 (BLOCK 7) | FLIPPP Easy |
| `oxygen.crew.1.pressure-below-400` | Crew oxygen 1 pressure below 400 psi | `F_OXYGEN_CREW_LOW` | 35 | Aircraft | 0.12.0-preview.4 | — |
| `oxygen.crew.1.pressure-below-800` | Crew oxygen 1 pressure below 800 psi | `F_OXYGEN_CREW_MED` | 35 | Aircraft | 0.12.0-preview.4 | — |
| `oxygen.crew.2.pressure-below-400` | Crew oxygen 2 pressure below 400 psi | `F_OXYGEN_CREW2_LOW` | 35 | Aircraft | 0.12.0-preview.4 | — |
| `oxygen.crew.2.pressure-below-800` | Crew oxygen 2 pressure below 800 psi | `F_OXYGEN_CREW2_MED` | 35 | Aircraft | 0.12.0-preview.4 | — |
| `oxygen.crew.supply-valve.1` | Crew oxygen supply valve 1 | `F_OXYGEN_CREW_SUPPLY_VALVE` | 35 | Aircraft | 0.12.0-preview.4 | — |
| `oxygen.crew.supply-valve.2` | Crew oxygen supply valve 2 | `F_OXYGEN_CREW_SUPPLY_VALVE2` | 35 | Aircraft | 0.12.0-preview.4 | — |
| `information.atsu` | ATSU | `F_INFO_ATSU` | 46 | Aircraft | 0.12.0-preview.4 | — |
| `doors.avionics.forward` | Forward avionics door | `F_DOOR_AVIONICS_1` | 52 | Aircraft | 0.12.0-preview.4 | — |
| `doors.avionics.left` | Left avionics door | `F_DOOR_AVIONICS_2` | 52 | Aircraft | 0.12.0-preview.4 | — |
| `doors.avionics.right` | Right avionics door | `F_DOOR_AVIONICS_3` | 52 | Aircraft | 0.12.0-preview.4 | — |
| `doors.entry.forward-left` | Forward left entry door | `F_DOOR_FWD_ENTRY_LEFT` | 52 | Aircraft | 0.9 (BLOCK 7) | fenixhangarweb component aliases |
| `doors.wing.left-1` | Left wing door 1 | `F_DOOR_LEFT_WING_1` | 52 | Aircraft | 0.12.0-preview.4 | — |
| `doors.wing.left-2` | Left wing door 2 | `F_DOOR_LEFT_WING_2` | 52 | Aircraft | 0.12.0-preview.4 | — |
| `doors.entry.aft-left` | Aft left entry door | `F_DOOR_AFT_ENTRY_LEFT` | 52 | Aircraft | 0.9 (BLOCK 7) | fenixhangarweb component aliases |
| `doors.entry.forward-right` | Forward right entry door | `F_DOOR_FWD_ENTRY_RIGHT` | 52 | Aircraft | 0.12.0-preview.4 | — |
| `doors.avionics.aft` | Aft avionics door | `F_DOOR_AVIONICS_4` | 52 | Aircraft | 0.12.0-preview.4 | — |
| `doors.cargo.forward` | Forward cargo door | `F_DOOR_FWD_CARGO` | 52 | Aircraft | 0.12.0-preview.4 | — |
| `doors.wing.right-1` | Right wing door 1 | `F_DOOR_RIGHT_WING_1` | 52 | Aircraft | 0.12.0-preview.4 | — |
| `doors.wing.right-2` | Right wing door 2 | `F_DOOR_RIGHT_WING_2` | 52 | Aircraft | 0.12.0-preview.4 | — |
| `doors.cargo.aft` | Aft cargo door | `F_DOOR_AFT_CARGO` | 52 | Aircraft | 0.12.0-preview.4 | — |
| `doors.cargo.bulk` | Bulk cargo door | `F_DOOR_BULK` | 52 | Aircraft | 0.12.0-preview.4 | — |
| `doors.entry.aft-right` | Aft right entry door | `F_DOOR_AFT_ENTRY_RIGHT` | 52 | Aircraft | 0.12.0-preview.4 | — |
| `engine.1.fadec.channel-a` | Engine 1 FADEC channel A | `F_ENGINE_FADEC_LEFT_A` | 70 | Engine 1 | 0.12.0-preview.4 | — |
| `engine.1.fadec.channel-b` | Engine 1 FADEC channel B | `F_ENGINE_FADEC_LEFT_B` | 70 | Engine 1 | 0.12.0-preview.4 | — |
| `engine.2.fadec.channel-a` | Engine 2 FADEC channel A | `F_ENGINE_FADEC_RIGHT_A` | 70 | Engine 2 | 0.12.0-preview.4 | — |
| `engine.2.fadec.channel-b` | Engine 2 FADEC channel B | `F_ENGINE_FADEC_RIGHT_B` | 70 | Engine 2 | 0.12.0-preview.4 | — |
| `engine.1.eiu` | Engine 1 interface unit (EIU 1) | `F_ENG1_EIU` | 70 | Engine 1 | 0.9 (BLOCK 7) | FSHANGAR fire-probe trials |
| `engine.2.eiu` | Engine 2 interface unit (EIU 2) | `F_ENG2_EIU` | 70 | Engine 2 | 0.12.0-preview.4 | — |
| `engine.1.surge` | Engine 1 surge | `F_ENGINE_1_SURGE` | 70 | Engine 1 | 0.9 (BLOCK 7) | FLIPPP Reckless |
| `engine.2.surge` | Engine 2 surge | `F_ENGINE_2_SURGE` | 70 | Engine 2 | 0.12.0-preview.4 | — |
| `engine.1.failure` | Engine 1 failure | `F_ENGINE_1` | 70 | Engine 1 | 0.12.0-preview.4 | — |
| `engine.2.failure` | Engine 2 failure | `F_ENGINE_2` | 70 | Engine 2 | 0.12.0-preview.4 | — |
| `engine.1.failure-with-damage` | Engine 1 failure with damage | `F_ENGINE_1_DAMAGE` | 70 | Engine 1 | 0.12.0-preview.4 | — |
| `engine.2.failure-with-damage` | Engine 2 failure with damage | `F_ENGINE_2_DAMAGE` | 70 | Engine 2 | 0.12.0-preview.4 | — |
| `engine.1.bird-strike` | Engine 1 bird strike | `F_ENGINE_1_BIRDSTRIKE` | 70 | Engine 1 | 0.12.0-preview.4 | — |
| `engine.2.bird-strike` | Engine 2 bird strike | `F_ENGINE_2_BIRDSTRIKE` | 70 | Engine 2 | 0.12.0-preview.4 | — |
| `engine.1.reverser.pressurized` | Engine 1 reverser pressurized | `F_REV_PRESS_ENG_1` | 70 | Engine 1 | 0.12.0-preview.4 | — |
| `engine.2.reverser.pressurized` | Engine 2 reverser pressurized | `F_REV_PRESS_ENG_2` | 70 | Engine 2 | 0.12.0-preview.4 | — |
| `engine.1.reverser.unlocked` | Engine 1 reverser unlocked | `F_REV_UNLOCK_ENG_1` | 70 | Engine 1 | 0.12.0-preview.4 | — |
| `engine.2.reverser.unlocked` | Engine 2 reverser unlocked | `F_REV_UNLOCK_ENG_2` | 70 | Engine 2 | 0.12.0-preview.4 | — |
| `engine.1.reverser.inhibited` | Engine 1 reverser inhibited by maintenance | `F_REV_INHIBIT_ENG_1` | 70 | Engine 1 | 0.12.0-preview.4 | — |
| `engine.2.reverser.inhibited` | Engine 2 reverser inhibited by maintenance | `F_REV_INHIBIT_ENG_2` | 70 | Engine 2 | 0.12.0-preview.4 | — |
| `engine.1.vibration.n1` | Engine 1 high N1 vibration | `F_VIB_N1_ENG_1` | 70 | Engine 1 | 0.9 (BLOCK 7) | FLIPPP Reckless |
| `engine.2.vibration.n1` | Engine 2 high N1 vibration | `F_VIB_N1_ENG_2` | 70 | Engine 2 | 0.12.0-preview.4 | — |
| `engine.1.vibration.n2` | Engine 1 high N2 vibration | `F_VIB_N2_ENG_1` | 70 | Engine 1 | 0.12.0-preview.4 | — |
| `engine.2.vibration.n2` | Engine 2 high N2 vibration | `F_VIB_N2_ENG_2` | 70 | Engine 2 | 0.12.0-preview.4 | — |
| `engine.1.oil-leak` | Engine 1 oil leak | `F_ENG1_OIL_LEAK` | 70 | Engine 1 | 0.12.0-preview.4 | — |
| `engine.2.oil-leak` | Engine 2 oil leak | `F_ENG2_OIL_LEAK` | 70 | Engine 2 | 0.12.0-preview.4 | — |
| `engine.1.reverser.shutoff-valve` | Engine 1 reverser shutoff valve | `F_HYD_REVERSER_SHUTOFF_VALVE_1` | 70 | Engine 1 | 0.12.0-preview.4 | — |
| `engine.2.reverser.shutoff-valve` | Engine 2 reverser shutoff valve | `F_HYD_REVERSER_SHUTOFF_VALVE_2` | 70 | Engine 2 | 0.12.0-preview.4 | — |

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
