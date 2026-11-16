using AirCity.Airfield;
using AirCity.Vehicles;
using UnityEngine;

namespace AirCity.Dev
{
    public partial class DevToolbar
    {
        string deicingNote;
        int deicingNoteArea;
        string deicingStaffNote;

        void DeicingInspector(Stand stand)
        {
            var service = airfield.airport.Deicing;
            var facility = service.For(stand);
            var job = service.JobFor(facility);
            var area = service.WorkAreaFor(facility);
            string status = job != null && !job.Completed ? job.Waiting ?? (job.IsSpraying ? "Treating aircraft" : "Trucks moving")
                : facility?.crewOnDuty == false ? "Crew off shift" : (facility?.machines ?? 0) == 0 ? "Needs trucks" : area == null ? "Needs supply" : "Ready for aircraft";
            DeicingInfoCard("DEICING", $"{facility?.machines ?? 0} trucks", status,
                job != null && !job.Completed && job.Waiting != null ? DevGui.Accent : DevGui.Success);
            if (job != null)
            {
                DeicingMeter("Wing treatment", $"{job.Progress:P0}", job.Progress);
                Note(job.aircraft != null ? job.aircraft.name : "Aircraft on pad");
            }
            PropText("Supply", area != null ? area.Name : "Unassigned");
            DeicingPrimary("Manage deicing", () => OpenDeicingForPad(stand), "Fleet, parking, supply and crew for this pad");
            if (area != null)
                Buttons(B("View supply", () => airfield.SelectDeicingWorkArea(area), "Select this pad's refill area"));
            if (airfield.DeicingPadNote != null) Danger(airfield.DeicingPadNote);
        }

        // Kept as small inspector pages for existing context actions. The airport window is the main workspace.
        void DeicingSupply(Stand stand)
        {
            var service = airfield.airport.Deicing;
            var facility = service.For(stand);
            DeicingFleetStatus(facility);
            DeicingPrimary("Open fleet management", () => OpenDeicingForPad(stand));
        }

        void DeicingWorkAreaInspector(DeicingWorkArea area)
        {
            var service = airfield.airport.Deicing;
            Title(area.Name);
            DeicingMeter("Fluid storage", $"{area.fluidLitres:N0} / {area.capacityLitres:N0} L", area.capacityLitres > 0f ? area.fluidLitres / area.capacityLitres : 0f);
            bool access = service.HasDeliveryAccess(area);
            DeicingStatus(access ? "Outside road connected" : "Outside road required", access ? DevGui.Success : DevGui.Accent);
            if (area.DeliveryPending) Note(DeicingDeliveryStatus(service, area));
            else
            {
                float batch = Mathf.Min(5000f, service.OrderCapacity(area));
                Buttons(B($"Order {batch:N0} L · ${AirportDeicing.DeliveryPrice(batch):N0}", () => OrderDeicingFluid(area, batch),
                    "Tanker delivery through the connected outside service road", enabled: access && batch >= 1f && service.Accounts.CanAfford(AirportDeicing.DeliveryPrice(batch))));
            }
            DeicingPrimary("Manage supply area", () => { OpenDeicingManagement(area); deicingManagementTab = 2; }, "Deliveries, recycling and assigned pads");
            Section("RECOVERY");
            PropText("Recycling", area.recyclingInstalled ? $"{area.recycledLitres:N0} L saved" : "Upgrade available");
            int pads = DeicingAssignedPadCount(service, area);
            PropText("Pads", $"{pads} assigned");
            bool busy = service.InUse(area);
            Space();
            Buttons(B("Move", () => airfield.BeginMoveDeicingWorkArea(area), "Move this idle supply area", enabled: !busy),
                B("Remove", () => airfield.DeleteSelection(), "Remove this idle supply area", enabled: !busy && !area.DeliveryPending, danger: true));
            if (busy) Note("In use. Wait for trucks to clear before moving.");
            if (deicingNoteArea == area.id && !string.IsNullOrEmpty(deicingNote)) Note(deicingNote);
            if (!string.IsNullOrEmpty(airfield.DeicingAreaNote)) Danger(airfield.DeicingAreaNote);
        }

        void DeicingWorkAreaToolInspector()
        {
            Title("Deicing supply");
            DeicingInfoCard("FLUID STORAGE", "30,000 L", $"${AirportDeicing.SupplyAreaPrice:N0}", DevGui.Primary);
            Note("Pave the station footprint. FILL and UNLOAD are service lanes; place truck parking separately.");
            Note("Connect a service road to outside access before ordering supplies. Several pads can share one area.");
            if (!string.IsNullOrEmpty(airfield.DeicingRoadSnapNote)) Note(airfield.DeicingRoadSnapNote);
            else Note("Roads snap to the lane ends. Hold Ctrl / Cmd for free placement.");
            if (!string.IsNullOrEmpty(airfield.DeicingAreaNote)) Danger(airfield.DeicingAreaNote);
        }

        void DeicingParkingToolInspector()
        {
            Title("Truck parking bay");
            DeicingInfoCard("DEICING FLEET", "One truck per bay", $"${AirportDeicing.ParkingBayPrice:N0} · 4.5 × 13 m", DevGui.Primary);
            Note("Place on a paved apron with room to drive in and out. Order a truck to this bay in Deicing operations.");
            Buttons(B("Cancel", () => airfield.SetTool(DevTool.Select), "Return to selection"));
            if (!string.IsNullOrEmpty(airfield.DeicingParkingNote)) Danger(airfield.DeicingParkingNote);
        }

        void DeicingOutsideAccessToolInspector()
        {
            Title("Outside service access");
            DeicingInfoCard("SUPPLIER CONNECTION", "Choose a road end", "Entry & exit", DevGui.Primary);
            Note("Mark the service-road endpoint that leads outside the airport. Connect supply areas to this road for deliveries and new trucks.");
            if (airfield.airport.Deicing.OutsideAccess.Count > 0)
                Buttons(B("Locate outside road", LocateDeicingOutsideRoad, "Move the camera to an existing outside entrance"));
            Buttons(B("Cancel", () => airfield.SetTool(DevTool.Select), "Return to selection"));
            if (!string.IsNullOrEmpty(airfield.DeicingAccessNote)) Danger(airfield.DeicingAccessNote);
        }

        void DeicingParkingInspector(DeicingParkingBay bay)
        {
            var service = airfield.airport.Deicing;
            var truck = service.TruckFor(bay);
            Title(bay.Name);
            DeicingInfoCard("TRUCK PARKING", truck == null ? "Available bay" : $"Truck {truck.index + 1}",
                truck?.Status ?? "Ready for a truck assignment", truck == null ? DevGui.Success : DevGui.Primary);
            DeicingWorkArea supply = null;
            foreach (var area in service.WorkAreas)
                if (area != null && area.id == bay.supplyAreaId) { supply = area; break; }
            PropText("Supply", supply?.Name ?? "Assigned when ordered");
            DeicingPrimary("Manage fleet", () =>
            {
                OpenDeicingManagement(supply);
                deicingManagementTab = 1;
            });
            Buttons(B("Move", () => airfield.BeginMoveDeicingParking(bay), "Move an unoccupied parking bay", enabled: truck == null),
                B("Remove", () => airfield.DeleteSelection(), "Remove an unoccupied parking bay", enabled: truck == null, danger: true));
            if (truck != null) Note("Reserved for this truck. Keep its access route clear.");
            else Note("Assign an existing truck or order a new one in fleet management.");
            if (!string.IsNullOrEmpty(airfield.DeicingParkingNote)) Danger(airfield.DeicingParkingNote);
        }

        void DeicingOutsideAccessInspector(DeicingOutsideAccess access)
        {
            Title("Outside service access");
            DeicingInfoCard("AIRPORT CONNECTION", "Supplier entry & exit", "Service road access", DevGui.Success);
            Note("Connected supply areas can receive fluid tankers and newly ordered trucks here.");
            Buttons(B("Locate outside road", LocateDeicingOutsideRoad, "Move the camera to the airport's outside road"));
            DeicingPrimary("Manage deicing", () => OpenDeicingManagement());
            Buttons(B("Remove access", () => airfield.DeleteSelection(), "Unmark this road's outside connection", danger: true));
            if (!string.IsNullOrEmpty(airfield.DeicingAccessNote)) Danger(airfield.DeicingAccessNote);
        }

        void LocateDeicingOutsideRoad()
        {
            var service = airfield.airport.Deicing;
            DeicingOutsideAccess entrance = null;
            float furthest = -1;
            foreach (var access in service.OutsideAccess)
            {
                if (access == null || airfield.airport.Taxiways.Node(access.nodeId) == null) continue;
                float distance = service.OutsideAccessPosition(access).sqrMagnitude;
                if (distance <= furthest) continue;
                furthest = distance; entrance = access;
            }
            if (entrance == null) return;
            var point = service.OutsideAccessPosition(entrance);
            var target = airfield.airport.transform.TransformPoint(new Vector3(point.x, 0, point.y));
            airfield.SelectDeicingOutsideAccess(entrance);
            var orbit = GetComponent<DevOrbitCamera>();
            if (airfield.game != null) airfield.game.view.FocusPoint(target, 140f);
            else if (orbit != null) { orbit.pivot = target; orbit.distance = 140; }
            else { transform.position = target + new Vector3(-70, 85, -90); transform.LookAt(target); }
        }

        DeicingFacility DeicingEquipment(Stand stand)
        {
            var airport = airfield.airport;
            if (airport.GetComponent<DeicingRig>() == null) airport.gameObject.AddComponent<DeicingRig>();
            return airport.Deicing.Configure(stand);
        }

        void OrderDeicingFluid(DeicingWorkArea area, float litres)
        {
            deicingNoteArea = area.id;
            deicingNote = airfield.airport.Deicing.OrderFluid(area, litres, out var problem)
                ? $"Ordered {litres:N0} L. Tanker delivery queued." : problem;
            if (DeicingManagementOpen) deicingManagementScroll = Vector2.zero;
        }

        void DeicingFleetStatus(DeicingFacility facility)
        {
            if (facility == null) return;
            var trucks = airfield.airport.Deicing.TrucksFor(facility);
            for (int i = 0; i < trucks.Count; i++)
            {
                var truck = trucks[i];
                DeicingMeter($"Truck {i + 1}", $"{truck.FluidLitres:N0} L", truck.FluidLitres / truck.CapacityLitres);
                Note(truck.Status);
            }
        }

        void DeicingStaffing(Stand stand)
        {
            var service = airfield.airport.Deicing;
            var facility = service.For(stand);
            if (facility == null) { Note("Assign trucks in fleet management first."); return; }
            PropText("Operators", $"{service.OperatorsFor(facility)} / {facility.machines} on shift");
            PropText("Base cost", $"${service.StandingHourlyCost(facility):N0} / hour");
            Buttons(B(facility.crewOnDuty ? "End shift" : "Start shift", () =>
                deicingStaffNote = service.SetCrewOnDuty(facility, !facility.crewOnDuty, out var problem) ? null : problem,
                "Shifts can end when treatment is complete and trucks are parked",
                enabled: facility.machines > 0 && (!facility.crewOnDuty || service.CanEndShift(facility))));
            if (!string.IsNullOrEmpty(deicingStaffNote)) Note(deicingStaffNote);
            DeicingPrimary("Manage crew & budget", () => OpenDeicingForPad(stand));
        }

        void DeicingRecycling(DeicingWorkArea area)
        {
            if (!area.recyclingInstalled)
            {
                DeicingInfoCard("RECYCLING UPGRADE", "Recover up to 60%", $"${AirportDeicing.RecyclingPrice:N0}", DevGui.Success);
                Note("Assigned pads connect automatically underground. Recovered fluid refills this tank.");
                Buttons(B("Buy recycling", () => BuyDeicingRecycling(area), "Full wing treatment; less new fluid needed",
                    enabled: !airfield.airport.Deicing.InUse(area) && airfield.airport.Deicing.Accounts.CanAfford(AirportDeicing.RecyclingPrice)));
            }
            else
            {
                PropText("Recovered", $"{area.recycledLitres:N0} L");
                PropText("Savings", $"${area.recycledLitres * AirportDeicing.FluidPricePerLitre:N0}");
                PropText("Processing", $"{area.runoffLitres:N0} L queued");
            }
        }

        void BuyDeicingRecycling(DeicingWorkArea area)
        {
            deicingNoteArea = area.id;
            deicingNote = airfield.airport.Deicing.BuyRecycling(area, out var problem)
                ? "Recycling installed. Assigned pads connected." : problem;
            if (DeicingManagementOpen) deicingManagementScroll = Vector2.zero;
        }

        void DeicingBudget()
        {
            var accounts = airfield.airport.Deicing.Accounts;
            PropText("Balance", $"${accounts.funds:N0}");
            PropText("Equipment", $"${accounts.capitalSpent:N0}");
            PropText("Fluid", $"${accounts.fluidSpent:N0}");
            PropText("Crew", $"${accounts.payrollSpent:0.00}");
            PropText("Running", $"${accounts.runningSpent:0.00}");
            Buttons(B(accounts.limitedBudget ? "Budget on" : "Sandbox spending", () => accounts.limitedBudget = !accounts.limitedBudget,
                "Both modes record costs. Budget mode blocks purchases above the balance.", on: accounts.limitedBudget));
            Buttons(B("Add $100,000", () => accounts.AddFunding(100000d), "Add sandbox deicing funds"));
        }

        void DeicingInfoCard(string label, string value, string status, Color tint)
        {
            Add(U(86f), r =>
            {
                DevGui.SurfaceCard(r);
                GUI.Label(new Rect(r.x + U(12f), r.y + U(8f), r.width - U(24f), U(16f)), label, DevGui.Caption);
                GUI.Label(new Rect(r.x + U(12f), r.y + U(26f), r.width - U(24f), U(25f)), value, DevGui.Title);
                DevGui.StatusPill(new Rect(r.x + U(12f), r.y + U(57f), r.width - U(24f), U(21f)), status, tint);
            });
        }

        void DeicingMeter(string label, string value, float progress)
        {
            Add(U(62f), r =>
            {
                DevGui.SurfaceCard(r);
                GUI.Label(new Rect(r.x + U(12f), r.y + U(7f), r.width - U(24f), U(18f)), label, DevGui.Small);
                GUI.Label(new Rect(r.x + U(12f), r.y + U(25f), r.width - U(24f), U(20f)), value, DevGui.Bold);
                DevGui.ProgressBar(new Rect(r.x + U(12f), r.yMax - U(10f), r.width - U(24f), U(5f)), progress, DevGui.Primary);
            });
        }

        void DeicingStatus(string text, Color tint) => Add(U(26f), r => DevGui.StatusPill(r, text, tint));

        void DeicingPrimary(string text, System.Action action, string tip = null) => Add(U(36f), r =>
        {
            if (DevGui.PrimaryButton(r, text, tip)) action();
        });

        static int DeicingAssignedPadCount(AirportDeicing service, DeicingWorkArea area)
        {
            int count = 0;
            foreach (var facility in service.Facilities)
                if (facility != null && service.Layout(facility) != null && service.WorkAreaFor(facility) == area) count++;
            return count;
        }

        static string DeicingDeliveryStatus(AirportDeicing service, DeicingWorkArea area)
        {
            foreach (var delivery in service.Deliveries)
                if (delivery.Area == area) return $"{area.deliveryLitres:N0} L ordered · {delivery.Status}";
            return $"{area.deliveryLitres:N0} L ordered · Supplier preparing";
        }
    }
}
