# Windows RUN telemetry → FSGAP parity matrix

FSGAP 0.12.0-preview.6. The authoritative reference for the Windows **W2** cutover: it maps every telemetry field the
released Windows v1.4.4 RUN pipeline emits (`SimConnectDataSource` → `LiveTelemetryMapper` → `SimConnectTelemetrySample`,
the `SIMCONNECT_V1` wire schema) to its FSGAP source, so the adapter can reproduce released behavior exactly from one
`FsgapRuntime` connection.

Status legend:

- **EXACT** — same quantity and unit; adapter copies the value (bool → 1.0/0.0 on the wire).
- **UNIT_CONVERSION** — same quantity, different unit; adapter applies the stated factor.
- **DERIVED_FROM_FSGAP_COLLECTION** — reconstructed from a normalized collection (gear units, flap surfaces,
  hydraulic systems, batteries) by selecting a member; no new SDK field.
- **MISSING_IN_FSGAP** — no equivalent existed; closed by a new normalized field in this preview.
- **NOT_ACTUALLY_POPULATED** — the wire field exists in the schema but v1.4.4 never assigns it (sent `null`); no SDK
  action.

Only fields that v1.4.4 actually populates (set in `LiveTelemetryMapper`) are parity-relevant. Everything FSGAP emits
is `Known` only when read and `Unavailable`/`Unknown` otherwise — never zero — so the adapter emits a wire value only
when the FSGAP value `IsKnown`.

## FAST group (≈1 Hz)

| Wire field | Legacy source SimVar | Legacy unit | FSGAP field | FSGAP unit | Adapter | Status |
|---|---|---|---|---|---|---|
| `sim_on_ground` | `SIM ON GROUND` | bool | `Flight.OnGround` | bool | bool→1/0 | EXACT |
| `agl_ft` | `PLANE ALT ABOVE GROUND` | ft | `Flight.HeightAboveGroundFeet` | ft | copy | EXACT |
| `ias_kt` | `AIRSPEED INDICATED` | kt | `Flight.IndicatedAirspeedKnots` | kt | copy | EXACT |
| `ground_speed_kt` | `GROUND VELOCITY` | kt | `Flight.GroundSpeedKnots` | kt | copy | EXACT |
| `vertical_speed_fpm` | `VERTICAL SPEED` (ft/s ×60) | fpm | `Flight.VerticalSpeedFeetPerMinute` | fpm | copy (FSGAP already ×60) | EXACT |
| `g_force` | `G FORCE` | g | `Flight.GLoad` | g | copy | EXACT |
| `pitch_deg` | `PLANE PITCH DEGREES` | deg, nose-up + | `Flight.PitchDegrees` | deg, nose-up + | copy | EXACT |
| `bank_deg` | `PLANE BANK DEGREES` | deg, right-wing-down + | `Flight.BankDegrees` | deg, right-wing-down + | copy | EXACT |
| `aoa_deg` | `INCIDENCE ALPHA` | deg | `Flight.AngleOfAttackDegrees` | deg | copy | EXACT |
| `total_weight_kg` | `TOTAL WEIGHT` | kg | `Flight.GrossWeightKilograms` | kg | copy | EXACT |
| `altitude_ft_msl` | `PLANE ALTITUDE` | ft | `Flight.AltitudeFeet` | ft | copy | EXACT |
| **`touchdown_normal_velocity_fps`** | **`PLANE TOUCHDOWN NORMAL VELOCITY`** | **ft/s, body-normal** | **`Flight.TouchdownNormalVelocityFeetPerSecond`** | **ft/s, body-normal** | **copy** | **MISSING_IN_FSGAP → added preview.6** |
| `acceleration_body_x` | `ACCELERATION BODY X` | g | `Flight.BodyAccelerationXG` | g | copy | EXACT |
| `acceleration_body_y` | `ACCELERATION BODY Y` | g | `Flight.BodyAccelerationYG` | g | copy | EXACT |
| `acceleration_body_z` | `ACCELERATION BODY Z` | g | `Flight.BodyAccelerationZG` | g | copy | EXACT |
| `aileron_left_pct` | `AILERON LEFT DEFLECTION PCT` | % | `FlightControls.AileronLeftDeflectionPercent` | % | copy | EXACT |
| `aileron_right_pct` | `AILERON RIGHT DEFLECTION PCT` | % | `FlightControls.AileronRightDeflectionPercent` | % | copy | EXACT |
| `elevator_pct` | `ELEVATOR DEFLECTION PCT` | % | `FlightControls.ElevatorDeflectionPercent` | % | copy | EXACT |
| `rudder_pct` | `RUDDER DEFLECTION PCT` | % | `FlightControls.RudderDeflectionPercent` | % | copy | EXACT |

### The touchdown field (the W2 blocker, now closed)

v1.4.4 sends the MSFS SimVar `PLANE TOUCHDOWN NORMAL VELOCITY` — the velocity **perpendicular to the aircraft body**
at the instant of touchdown, in **feet per second** — raw. FSGAP already read this SimVar on the shared connection but
only exposed it as `Flight.TouchdownVerticalSpeedFeetPerMinute` (`-abs(fps × 60)`, a per-minute, sign-normalized
value). Preview.6 adds `Flight.TouchdownNormalVelocityFeetPerSecond`, the **raw body-normal ft/s** value, mapped
straight from the SimVar — independent of the per-minute field (neither is derived from the other). The adapter copies
it to `touchdown_normal_velocity_fps` with no conversion.

> Note on the existing field: `TouchdownVerticalSpeedFeetPerMinute` is derived from the same SimVar; it is a per-minute,
> always-negative presentation, **not** an independent world-vertical sensor. W2 maps the wire from the new ft/s field,
> never from the per-minute one.

## SYSTEMS group (≈5 s legacy; FSGAP reads the configuration group faster)

| Wire field | Legacy source SimVar | Legacy unit | FSGAP field | FSGAP unit | Adapter | Status |
|---|---|---|---|---|---|---|
| `gear_position_pct` | `GEAR CENTER POSITION` (center = nose) | % | `LandingGear.Units[id="nose"].ExtensionPercent` | % | select `nose` unit | DERIVED_FROM_FSGAP_COLLECTION |
| `gear_handle_down` | `GEAR HANDLE POSITION` | bool/% | `LandingGear.HandleDown` | bool | bool→1/0 | EXACT |
| `flaps_position_pct` | `FLAPS HANDLE PERCENT` | % | `FlightControls.FlapsHandlePercent` | % | copy | EXACT |
| `flaps_left_pct` | `TRAILING EDGE FLAPS LEFT PERCENT` | % | `FlightControls.FlapSurfaces[id="trailing-left"].ExtensionPercent` | % | select surface | DERIVED_FROM_FSGAP_COLLECTION |
| `flaps_right_pct` | `TRAILING EDGE FLAPS RIGHT PERCENT` | % | `FlightControls.FlapSurfaces[id="trailing-right"].ExtensionPercent` | % | select surface | DERIVED_FROM_FSGAP_COLLECTION |
| `steer_input_pct` | `STEER INPUT CONTROL` | % | `LandingGear.SteeringInputPercent` | % | copy | EXACT |
| `antiskid_active` | `ANTISKID BRAKES ACTIVE` | bool | `LandingGear.AntiskidActive` | bool | bool→1/0 | EXACT |
| `brake_left_pct` | `BRAKE LEFT POSITION` | % (0–100) | `LandingGear.BrakeLeftPercent` | % (0–100) | copy — **do NOT ×100** | EXACT |
| `brake_right_pct` | `BRAKE RIGHT POSITION` | % (0–100) | `LandingGear.BrakeRightPercent` | % (0–100) | copy — **do NOT ×100** | EXACT |
| `reverse_1_engaged` | engine reverser | bool | `Engines[0].ReverserEngaged` | bool | bool→1/0 | EXACT |
| `reverse_2_engaged` | engine reverser | bool | `Engines[1].ReverserEngaged` | bool | bool→1/0 | EXACT |
| `overspeed_warning` | `OVERSPEED WARNING` | bool | `Warnings.Overspeed` | bool | bool→1/0 | EXACT |
| `stall_warning` | `STALL WARNING` | bool | `Warnings.Stall` | bool | bool→1/0 | EXACT |
| `flap_speed_exceeded` | flap overspeed | bool | `Warnings.FlapSpeedExceeded` | bool | bool→1/0 | EXACT |
| `gear_speed_exceeded` | gear overspeed | bool | `Warnings.GearSpeedExceeded` | bool | bool→1/0 | EXACT |
| `engine{1,2}_combustion` | `GENERAL ENG COMBUSTION:{n}` | bool | `Engines[n-1].Running` | bool | bool→1/0 | EXACT |
| `engine{1,2}_starter_active` | `GENERAL ENG STARTER ACTIVE:{n}` | bool | `Engines[n-1].StarterActive` | bool | bool→1/0 | EXACT |
| `eng{1,2}_n1_pct` | `TURB ENG N1:{n}` | % | `Engines[n-1].N1Percent` | % | copy | EXACT |
| `eng{1,2}_n2_pct` | `TURB ENG N2:{n}` | % | `Engines[n-1].N2Percent` | % | copy | EXACT |
| `eng{1,2}_egt_c` | `GENERAL ENG EXHAUST GAS TEMPERATURE:{n}` | °C | `Engines[n-1].EgtCelsius` | °C | copy | EXACT |
| `eng{1,2}_oil_temp_c` | `GENERAL ENG OIL TEMPERATURE:{n}` | °C | `Engines[n-1].OilTemperatureCelsius` | °C | copy | EXACT |
| `eng{1,2}_oil_pressure_psi` | `GENERAL ENG OIL PRESSURE:{n}` | psi | `Engines[n-1].OilPressurePsi` | psi | copy | EXACT |
| `eng{1,2}_throttle_pct` | throttle lever | % | `Engines[n-1].ThrottleLeverPercent` | % | copy | EXACT |
| `eng{1,2}_fuel_flow_pph` | `ENG FUEL FLOW PPH:{n}` | lb/h | `Engines[n-1].FuelFlowKilogramsPerHour` | kg/h | **× 2.2046226218** (kg/h → lb/h) | UNIT_CONVERSION |
| `apu_bleed_active` | `PNEUMATICS APU BLEED AIR` | bool | `Apu.BleedOn` | bool | bool→1/0 | EXACT |
| `cabin_altitude_ft` | cabin altitude | ft | `Pressurization.CabinAltitudeFeet` | ft | copy | EXACT |
| `cabin_altitude_rate_fps` | cabin altitude rate | ft/s | `Pressurization.CabinAltitudeRateFeetPerMinute` | fpm | **÷ 60** (fpm → ft/s) | UNIT_CONVERSION |
| `hyd_pump_pressure_green` | green system pressure | psi | `HydraulicSystems[green].PressurePsi` | psi | select system | DERIVED_FROM_FSGAP_COLLECTION |
| `hyd_pump_pressure_blue` | blue system pressure | psi | `HydraulicSystems[blue].PressurePsi` | psi | select system | DERIVED_FROM_FSGAP_COLLECTION |
| `hyd_reservoir_pct_green` | green reservoir | % | `HydraulicSystems[green].ReservoirPercent` | % | select system | DERIVED_FROM_FSGAP_COLLECTION |
| `hyd_reservoir_pct_blue` | blue reservoir | % | `HydraulicSystems[blue].ReservoirPercent` | % | select system | DERIVED_FROM_FSGAP_COLLECTION |
| `battery_voltage_bat1` | battery 1 voltage | V | `Batteries[0].VoltageVolts` | V | select battery | DERIVED_FROM_FSGAP_COLLECTION |

> Fuel-flow round-trip: FSGAP reads `ENG FUEL FLOW PPH` and stores kg/h (`× 0.45359237`); the Windows adapter converts
> back to lb/h (`× 2.2046226218`). The two factors are reciprocals, so the wire value matches the released lb/h figure
> to floating-point precision.

## ENVIRONMENT group (≈10 s)

| Wire field | Legacy source SimVar | Legacy unit | FSGAP field | FSGAP unit | Adapter | Status |
|---|---|---|---|---|---|---|
| `latitude_deg` | `PLANE LATITUDE` | deg | `Flight.LatitudeDegrees` | deg | copy | EXACT |
| `longitude_deg` | `PLANE LONGITUDE` | deg | `Flight.LongitudeDegrees` | deg | copy | EXACT |
| `ambient_temp_c` | ambient temperature | °C | `Environment.OutsideAirTemperatureCelsius` | °C | copy | EXACT |
| `wind_direction_deg` | wind direction (true) | deg true | `Environment.WindDirectionDegreesTrue` | deg true | copy | EXACT |
| `wind_speed_kt` | wind velocity | kt | `Environment.WindSpeedKnots` | kt | copy | EXACT |
| `precip_state` | precipitation state | code | `Environment.Precipitation` | enum | map enum → code | DERIVED |
| `precip_rate_mm_h` | precipitation rate | mm | `Environment.PrecipitationRateMillimeters` | mm | copy | EXACT |

## Meta

| Wire field | Source | Notes |
|---|---|---|
| `elapsed_seconds` | client recording clock | NOT telemetry — supplied by the Windows caller (`FsgapTelemetryAdapter.ToWireSample(telemetry, elapsedSeconds)`). |
| `timestamp_utc` | `AircraftTelemetry.Timestamp` | copy |

## NOT_ACTUALLY_POPULATED by v1.4.4 (no SDK action)

`LiveTelemetryMapper` never assigns these, so the released client sends `null`; the wear-v2 schema carries them for the
server but v1.4.4 does not feed them. Adding FSGAP fields for them would be speculative, so this preview does not.

- **APU:** `apu_rpm_pct` (`APU PCT RPM`), `apu_starter_pct` (`APU PCT STARTER`), `apu_generator_active`
  (`APU GENERATOR ACTIVE`) — registry status **Unavailable**, confirmed to read 0 on 2026-08-31 even with the Fenix APU
  fully online. Dead on Fenix; not emitted.
- **`gear_skidding_factor`** — no matching stock SimVar exists; never wired.
- **Flight-control / gear extras:** `spoiler_left_pct`, `spoiler_right_pct`, `slats_left_pct`, `slats_right_pct`.
- **Pressurization:** `cabin_diff_pressure`.
- **Hydraulics (yellow + detail):** `hyd_pump_pressure_yellow`, `hyd_pump_active_*`, `hyd_reservoir_pct_yellow`,
  `hyd_reservoir_pressure_psi_*`, `hyd_ptu_active`, `hyd_ptu_pressure_psi`.
- **Electrical:** `battery_voltage_bat2`, `battery_load_amps_*`, `battery_capacity_pct_*`, `electrical_bus_*`,
  `electrical_total_load_amps`.
- **Misc:** `eng1_anti_ice`, `eng2_anti_ice`, `brake_temperature_left`, `brake_temperature_right`, `structural_ice_pct`.

If a future Windows version starts populating any of these from a trusted source, raise it as its own normalized SDK
gap then — do not pre-add.

## Secondary-candidate audit outcome (preview.6)

The four fields flagged during the Windows W2 audit were all classified **NOT_ACTUALLY_POPULATED** (above) and are
therefore **not** added to FSGAP: `apu_rpm_pct`, `apu_starter_pct`, `apu_generator_active`, `gear_skidding_factor`.

The gear/flaps candidates need **no** new aggregate SDK field: `flaps_position_pct` is `FlightControls.FlapsHandlePercent`
exactly; `flaps_left/right_pct` and `gear_position_pct` are reconstructed from the existing `FlapSurfaces` / gear `Units`
collections by selecting the `trailing-left`/`trailing-right` surfaces and the `nose` unit.

The **only** SDK gap closed by preview.6 is `touchdown_normal_velocity_fps`.
