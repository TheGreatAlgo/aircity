# Airport buildings roadmap

Recorded: 2026-11-16.

## Purpose and status

Plan building families that make AirCity interesting from a small airfield to a major international hub.
The user's starting interests are a training centre, a private aircraft terminal and hangars.
This document preserves the building ideas and recommended priorities from that discussion for future agents.

This roadmap tracks playable buildings and proposed expansions. The five stages organise ideas rather than
define mandatory unlock tiers. Costs, capacities, staffing, contracts and progression remain provisional unless
a system doc specifies them.

## Playable buildings and access

| Building | In the game today | Connection |
|---|---|---|
| [Small rentable hangar](systems/hangars.md) | One light-aircraft bay with a physical resident, ground transfers and Career rent. The hangar can be built before its taxiway. | Run a straight, level aircraft taxiway across the doors, about 10–11 m from the threshold to the near pavement edge (12 m maximum). The concrete approach appears when the connection is clear. Aircraft reach stands over the taxiway network. |
| [Executive hangar](systems/hangars.md) | One 84 × 42 m clear-span building with an 80 m lift door and three independently leased parking spots for business jets or general aviation. | Snap a straight, level paved taxiway across the shared apron at a 38 m setback. Use code B or wider according to the aircraft. |
| [Airline hangar](systems/hangars.md) | One 92 × 90 m clear-span bay for commercial aircraft, including Starling widebody and double-deck airliners. | Snap a straight, level paved taxiway across the door at a 95 m setback. Use code C or wider according to the aircraft. |
| [Visitor centre](systems/visitor-centre.md) | One reception building and an 18-bay car park. Individual visits and parking income are future work. | The placement quote includes a two-way driveway from the parking lot to an outside-connected public road within 95 m. The parking entrance is separate from the original airport progression entrance. |

The hangar approach is airside and the visitor driveway is landside. Lay out their pavement independently
and leave clearance between the hangar, its approach, and the visitor centre and car park.

The Hangar tool snaps the doorway to an existing taxiway, and the Taxiway tool snaps to the two sides
of a disconnected hangar's approach. The Street tool snaps to the visitor centre's driveway mouth and
aligns a rebuilt connection with its parking aisle.

On 2026-11-17, 176 related Unity EditMode checks passed, covering placement, road and taxiway clearance,
terrain and saved layouts. Rendered reviews confirmed a hangar-to-stand round trip and the visitor centre's
two-way driveway. The test report is in `Logs/building-connections-final.xml`.
The follow-up snapping review passed 188 EditMode checks, including diagonal hangar placement, drawing
a taxiway across a hangar, and rebuilding a rotated visitor driveway from either end.
The report is in `Logs/building-snapping-final.xml`.

## Design direction

- A small airport should remain enjoyable indefinitely. Growth into an international hub is one path;
  training, private aviation, maintenance, cargo and tourism offer other destinations.
- Give each operational building a visible purpose: resident aircraft, new flight activity, services,
  storage, staff, deliveries, contracts or passenger flows.
- Connect income to work and capacity. A hangar earns rent by accommodating aircraft; a workshop earns
  maintenance income by occupying bays, using parts and assigning mechanics.
- Separate building size from business role. A small premium terminal can serve private customers;
  a large hangar can provide leased storage or maintenance bays.
- Offer expandable building families. Players should be able to keep early buildings, add capacity and
  create separate districts rather than repeatedly replace the entire airport.
- Use modules inside larger buildings where appropriate. Lounges, border processing, briefing rooms and
  staff facilities do not all need independent exterior buildings.
- Distinguish pilot training, professional simulator training and airport staff training. Each can have
  its own role as the airport develops.

## Stage 1: small airfield

Character: grass or modest paved runway, light aircraft, small buildings and a few local businesses.

| Building | Proposed gameplay |
|---|---|
| Airfield clubhouse / reception | Extend the playable visitor centre with visiting-pilot reception, flight bookings, basic administration and a small café. |
| Flying school | Student and instructor activity, training contracts and regular flights. Training circuits compete with visitors for runway time. |
| Small aircraft hangars | Extend the playable single-bay rental hangar with more unit and door arrangements. |
| Light aircraft maintenance workshop | Inspections and repairs requiring mechanics, spare parts and available workshop space. |
| Fuel hut and tank | Fuel sales and supplier deliveries; storage limits affect how much traffic can be served. |
| Small equipment shed | Storage and parking for a mower, utility vehicle and basic airfield equipment. |
| Scenic flight office | Sightseeing departures, with demand influenced by location, season and weather. |
| Pilot campsite / cabins | Accommodation for visitors at scenic or remote airfields. |

Experience goal: recognisable resident aircraft, students learning to fly and visiting pilots make the
airfield feel inhabited even when traffic is light.

## Stage 2: local airport and private aviation

Character: charter flights, business visitors, resident aircraft and the first scheduled passenger services.

| Building | Proposed gameplay |
|---|---|
| Private aviation terminal / FBO | A fixed-base operator serving visiting private aircraft with reception, lounge, handling and fuel. Higher service expectations support higher fees. |
| Charter operator office | Business trips, holiday charters and air taxi contracts. |
| Executive hangar | Extend the playable shared-storage building with attached offices and dedicated private aviation tenants. |
| Shared storage hangar | Multiple aircraft share a building; efficient storage introduces access and towing decisions. |
| Expanded maintenance hangar | Larger aircraft, longer jobs and specialist mechanics. Aircraft occupy bays during work. |
| Air ambulance base | Urgent arrivals and departures reward reliable access and operational readiness. |
| Small passenger terminal | Waiting area, check-in and walking access to aircraft for the first scheduled routes. |
| Small control tower | A staffed airport operations role as traffic grows; extend the existing tower assets and traffic system. |

Keep the FBO distinct from the scheduled passenger terminal. A dedicated private aviation apron gives
players another district to design and another business to operate.

## Stage 3: regional commercial airport

Character: regular passenger schedules, more staff and increasingly important turnaround services.

| Building | Proposed gameplay |
|---|---|
| Expandable passenger terminal | More passenger processing capacity, gate lounges, shops and services. |
| Baggage hall | Extend existing baggage teams, equipment, queues and terminal links as flight demand grows. |
| Ground handling depot | Parking, maintenance and dispatch for stairs, buses, tugs and other handling equipment. |
| Catering kitchen | Flight meal production, supply deliveries and timed dispatch to aircraft. |
| Aircraft cleaning and supplies depot | Cleaning crews, cabin supplies and turnaround jobs. |
| Airport fire station | Vehicles, crews and training; placement affects response coverage. |
| Small cargo / mail warehouse | Mail, parcel and freight contracts, including activity outside passenger peaks. |
| Staff centre | Changing rooms, meals, rest facilities and staff transport. |
| Car rental centre | Landside revenue, vehicle storage and shuttle traffic. |

Experience goal: a busy departure period stretches baggage, catering, stands and staff together, making
the arrangement of support buildings matter.

## Stage 4: international airport

Character: international processing, larger aircraft, connecting passengers and substantial logistics.

| Building or module | Proposed gameplay |
|---|---|
| International terminal expansion | Space for international processing and distinct passenger flows. |
| Border processing facilities | Immigration and customs capacity, preferably available as terminal modules. |
| Transfer concourse | Connecting passengers create demand between flights; layout and delays affect connections. |
| Airline lounge / premium terminal | Premium contracts with service and access expectations. |
| Large aircraft maintenance hangar | Larger bays, specialist equipment and valuable maintenance contracts. |
| Cargo terminal | Dedicated truck access, freight storage, loading areas and cargo aircraft stands. |
| Fuel farm | Bulk deliveries, storage and distribution, with supply and operating cost decisions. |
| Deicing centre | Extend the existing deicing facilities into a larger winter operation. |
| Airport hotel | Accommodation for crews, early departures, overnight connections and disrupted passengers. |

## Stage 5: major international hub

Character: several airport districts, major tenants and complex connections between facilities.

| Building | Proposed gameplay |
|---|---|
| Satellite terminal | More gates and a need to move passengers between buildings. |
| Airport operations centre | Airport-wide coordination of stands, resources and disruption recovery. |
| Airline base / headquarters | A major tenant brings dependable traffic, dedicated facilities and contractual commitments. |
| Crew reporting and rest centre | Supports aircraft and crews based at the airport. |
| Flight simulator academy | Professional training business distinct from the beginner flying school. |
| Heavy maintenance complex | Multiple bays, workshops, parts stores and long aircraft stays. |
| Express parcel hub | Freight arrival banks, parcel sorting and departure banks, potentially with substantial night activity. |
| Rail station / transport interchange | A reason to invest in public transport as passenger volumes grow. |
| Utility and energy centre | Power, backup generation and heating/cooling capacity for a large airport campus. |

## Optional specialisations

These branches can overlap and do not require reaching the largest airport stage.

| Airport direction | Buildings and facilities |
|---|---|
| Pilot training | Flying school, briefing rooms, instructor offices, simulator centre, student accommodation and trainer hangars. |
| Private aviation | FBO, executive hangars, VIP lounge, charter offices and aircraft detailing hangar. |
| Cargo | Warehouses, parcel sorting hub, cold storage, specialist freight facilities and truck staging areas. |
| Maintenance | Repair hangars, engine workshop, parts warehouse, paint hangar and aircraft storage facilities. |
| Tourism | Scenic flight office, skydiving centre, helicopter tours, visitor centre and hotel. |
| Remote community | Mail shed, medical flight base, essential freight warehouse and small passenger shelter. |
| Aviation destination | Museum, restored aircraft hangar, observation terrace, restaurant and airshow facilities. |

Helicopter tours, skydiving and similar activities would need their own supporting simulation; their
presence in this list does not imply those aircraft or activities already exist.

## Recommended follow-up additions

Suggested order, subject to the user's next feature choice:

| Priority | Building family | Why start here | Suggested first playable scope |
|---|---|---|---|
| 1 | Flying school | Gives the smallest aircraft a purpose and creates recognisable daily activity. | Place a school, assign training aircraft and run training activity through shared traffic. Start with supported flight behaviour; circuits need a separate traffic extension. |
| 2 | Private terminal / FBO | Creates a second passenger business alongside scheduled airlines. | Connect reception to a private apron, assign eligible visiting flights and show service activity and capacity. |
| 3 | Maintenance workshop / hangar | Gives aircraft a reason to stay and makes bay occupancy meaningful. | Assign a supported aircraft to a bay and simulate a timed inspection with staff/parts requirements as those systems become available. Towing into buildings is a separate movement requirement. |
| 4 | Fuel facilities | Connects aircraft services to storage, deliveries and road access. | Store fuel, receive deliveries and serve compatible aircraft through a defined service workflow. |
| 5 | Small cargo warehouse | Adds another traffic business before the airport becomes large. | Connect a warehouse to road access and freight handling, then run a small delivery or mail contract. |

These scopes are implementation starting points, not claims that the necessary tenant, economy,
training, towing or fuel systems already exist. When choosing a feature, inspect those dependencies
and define a complete playable slice before implementation.

## Existing foundations to reuse

The project already documents shared construction, terminal geometry, aircraft traffic, service roads,
maintenance depots, baggage and deicing. Extend those systems rather than introduce parallel versions.
Some buildings listed above already have assets or partial systems; the roadmap proposes additional
business roles and scale as well as entirely new buildings.

| Existing documentation | Relevance |
|---|---|
| [New Game and shared session](systems/new-game.md) | Career/Sandbox rules, construction, progression, game assets and full-airport save/load. |
| [Gates and terminal buildings](systems/gates.md) | Terminal/concourse geometry, facades, floors, placement and bridge connections. |
| [Airfield](systems/airfield.md) | Runways, taxiways, stands, aprons and aircraft compatibility. |
| [Public roads](systems/public-roads.md) | Landside entrances and the separate public-road graph. |
| [Traffic](systems/traffic.md) | Shared aircraft movement, routing, holds and clearances. |
| [Maintenance](systems/maintenance.md) | Vehicle depots, dispatch, teams and existing push-back behaviour. |
| [Baggage](systems/baggage.md) and [terminal baggage](systems/terminal-baggage.md) | Halls, equipment, teams and terminal links. |
| [Deicing](systems/deicing.md) | Supply areas, storage, deliveries, equipment and service holds. |
| [Control tower assets](assets/ControlTowers.md) | Existing tower models. |
| [Art source guide](../ArtSource/README.md) | Generated asset pipeline. |

## Handoff for the next building feature

1. Read `CLAUDE.md`, `Docs/README.md` and the relevant system docs. Check current implementation;
   older status tables may lag behind the code.
2. Establish the building's footprint, aircraft eligibility, access needs, visible activity and first
   playable business role. Keep exact balance values provisional until exercised in play.
3. Use the same airport data, build tools, simulation and save system in Career and Sandbox.
   Mode rules control prices and progression.
4. Generate new art through the project's source pipeline. Keep dimensions and gameplay interfaces
   in data rather than deriving operational rules from meshes.
5. Validate placement, access, aircraft clearance and save/load for the chosen feature. Check visual
   quality with close-up renders and airport-scale views; use meaningful simulation tests where needed.
6. Update the relevant system/asset docs and this roadmap with actual implemented scope and remaining work.

Open design choices to resolve when relevant: expanded hangar tenant ownership, shared-bay indoor aircraft movement,
pilot versus staff training progression, contract structure, utility depth, passenger interior simulation
and how far each building family supports modular expansion.
