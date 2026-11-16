using System;
using System.Collections.Generic;
using AirCity.Airfield;
using AirCity.Vehicles;
using UnityEngine;

namespace AirCity.Dev
{
    public partial class DevToolbar
    {
        bool deicingManagementOpen;
        int deicingManagementTab, deicingManagementArea, deicingManagementPad, deicingAssigningPad, deicingOrderingPad;
        Vector2 deicingManagementScroll, deicingAreasScroll;
        string deicingManagementNote;
        DeicingTruckState deicingParkingTruck;
        public bool DeicingManagementOpen => deicingManagementOpen;
        public void CloseDeicingManagement() => deicingManagementOpen = false;

        /// <summary>The airport-wide deicing workspace. Selecting an area only filters the view.</summary>
        public void OpenDeicingManagement(DeicingWorkArea area = null)
        {
            if (airfield == null) airfield = GetComponent<DevAirfieldTool>();
            if (airfield == null || airfield.airport == null) return;
            deicingManagementOpen = true;
            deicingManagementArea = area?.id ?? 0;
            deicingManagementPad = deicingAssigningPad = deicingOrderingPad = 0;
            deicingParkingTruck = null;
            deicingManagementTab = 0;
            deicingManagementScroll = Vector2.zero;
            deicingManagementNote = null;
            deicingNote = null;
            openPop = null;
            gateDesigner.Close();
            airfield.SetTool(DevTool.Select);
        }

        void OpenDeicingForPad(Stand stand)
        {
            OpenDeicingManagement(airfield.airport.Deicing.WorkAreaFor(airfield.airport.Deicing.For(stand)));
            deicingManagementPad = stand.id;
            deicingManagementTab = 1;
        }

        void DrawDeicingManagement()
        {
            if (!deicingManagementOpen || airfield == null || airfield.airport == null) return;
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                deicingManagementOpen = false;
                Event.current.Use();
                return;
            }
            var service = airfield.airport.Deicing;
            var area = DeicingSelectedArea(service);
            float width = Mathf.Min(U(920f), Screen.width - U(32f));
            float height = Mathf.Min(U(660f), Screen.height - U(100f));
            var panel = new Rect(Mathf.Round((Screen.width - width) * .5f), Mathf.Round((Screen.height - height) * .5f) - U(18f), width, height);
            DevGui.Panel(panel);
            float pad = U(20f), leftWidth = Mathf.Min(U(204f), width * .25f), gap = U(18f);
            var iconRect = new Rect(panel.x + pad, panel.y + pad, U(34f), U(34f));
            DevGui.Fill(iconRect, DevGui.GUI(DevGui.Primary), U(10f));
            DevGui.DrawIcon(new Rect(iconRect.x + U(7f), iconRect.y + U(7f), U(20f), U(20f)), DevGui.Icon("deice"), Color.white);
            GUI.Label(new Rect(iconRect.xMax + U(12f), iconRect.y - U(1f), panel.width - U(132f), U(24f)), "Deicing operations", DevGui.Title);
            GUI.Label(new Rect(iconRect.xMax + U(12f), iconRect.y + U(22f), panel.width - U(132f), U(18f)), "Keep the fleet ready and the fluid flowing", DevGui.Small);
            if (DevGui.Button(new Rect(panel.xMax - U(48f), panel.y + U(16f), U(32f), U(32f)), "×", "Close deicing management"))
                deicingManagementOpen = false;

            float top = panel.y + U(78f), bottom = panel.yMax - U(20f);
            var rail = new Rect(panel.x + pad, top, leftWidth, bottom - top);
            DeicingAreaRail(rail, service);
            float contentX = rail.xMax + gap;
            float contentW = panel.xMax - pad - contentX;
            var tabs = new Rect(contentX, top, contentW, U(36f));
            string[] labels = { "Overview", "Fleet & pads", "Supply" };
            for (int i = 0; i < labels.Length; i++)
            {
                float tabWidth = (tabs.width - U(12f)) / 3f;
                if (DevGui.Button(new Rect(tabs.x + i * (tabWidth + U(6f)), tabs.y, tabWidth, tabs.height), labels[i], on: deicingManagementTab == i))
                {
                    deicingManagementTab = i;
                    deicingManagementScroll = Vector2.zero;
                }
            }
            var viewport = new Rect(contentX, tabs.yMax + U(16f), contentW, bottom - tabs.yMax - U(16f));
            // Scroll content is measured using the exact same rows that draw it.
            var rows = new List<(float height, Action<Rect> draw)>();
            float rowWidth = viewport.width - U(18f);
            if (!string.IsNullOrEmpty(deicingManagementNote)) DText(rows, deicingManagementNote, rowWidth, true);
            if (deicingNoteArea == area?.id && !string.IsNullOrEmpty(deicingNote)) DText(rows, deicingNote, rowWidth, true);
            if (deicingManagementTab == 0) DeicingOverviewRows(rows, service, area, rowWidth);
            else if (deicingManagementTab == 1) DeicingFleetRows(rows, service, area, rowWidth);
            else DeicingSupplyRows(rows, service, area, rowWidth);
            float contentHeight = U(4f);
            foreach (var row in rows) contentHeight += row.height + U(10f);
            DevGui.Cover(viewport);
            deicingManagementScroll = GUI.BeginScrollView(viewport, deicingManagementScroll, new Rect(0f, 0f, rowWidth, contentHeight), false, false);
            float y = U(2f);
            foreach (var row in rows)
            {
                row.draw(new Rect(0f, y, rowWidth, row.height));
                y += row.height + U(10f);
            }
            GUI.EndScrollView();
        }

        DeicingWorkArea DeicingSelectedArea(AirportDeicing service)
        {
            foreach (var area in service.WorkAreas)
                if (area != null && area.id == deicingManagementArea) return area;
            deicingManagementArea = 0;
            return null;
        }

        void DeicingAreaRail(Rect rail, AirportDeicing service)
        {
            DevGui.SurfaceCard(rail);
            float inset = U(12f);
            GUI.Label(new Rect(rail.x + inset, rail.y + U(12f), rail.width - inset * 2f, U(18f)), "SUPPLY AREAS", DevGui.Caption);
            var viewport = new Rect(rail.x + inset, rail.y + U(40f), rail.width - inset * 2f, rail.height - U(106f));
            float h = U(52f) * (service.WorkAreas.Count + 1);
            DevGui.Cover(viewport);
            deicingAreasScroll = GUI.BeginScrollView(viewport, deicingAreasScroll, new Rect(0f, 0f, viewport.width - U(14f), h), false, false);
            float y = 0f;
            if (DevGui.Button(new Rect(0f, y, viewport.width - U(14f), U(42f)), "All airport areas", on: deicingManagementArea == 0))
                SelectDeicingManagementArea(0);
            y += U(52f);
            foreach (var item in service.WorkAreas)
            {
                if (item == null) continue;
                if (DevGui.Button(new Rect(0f, y, viewport.width - U(14f), U(42f)), item.Name, $"{item.fluidLitres:N0} L in storage", on: deicingManagementArea == item.id))
                    SelectDeicingManagementArea(item.id);
                y += U(52f);
            }
            GUI.EndScrollView();
            if (DevGui.PrimaryButton(new Rect(rail.x + inset, rail.yMax - U(51f), rail.width - inset * 2f, U(36f)), "+ Supply area", $"Build storage · ${AirportDeicing.SupplyAreaPrice:N0}"))
            {
                deicingManagementOpen = false;
                airfield.BeginDeicingWorkArea();
            }
        }

        void SelectDeicingManagementArea(int id)
        {
            deicingManagementArea = id;
            deicingManagementPad = deicingAssigningPad = deicingOrderingPad = 0;
            deicingParkingTruck = null;
            deicingManagementScroll = Vector2.zero;
            deicingManagementNote = null;
        }

        void DeicingOverviewRows(List<(float, Action<Rect>)> rows, AirportDeicing service, DeicingWorkArea area, float width)
        {
            float stock = 0f, capacity = 0f;
            int trucks = 0, pads = 0, treating = 0;
            foreach (var item in service.WorkAreas)
                if (item != null && (area == null || area == item)) { stock += item.fluidLitres; capacity += item.capacityLitres; }
            foreach (var stand in airfield.airport.Taxiways.stands)
            {
                if (!stand.DeIcing) continue;
                var facility = service.For(stand);
                if (area != null && service.WorkAreaFor(facility) != area) continue;
                pads++;
                trucks += facility?.machines ?? 0;
                if (service.JobFor(facility) is DeicingJob job && !job.Completed) treating++;
            }
            DHeading(rows, area?.Name ?? "Airport overview", "Equipment and supply at a glance");
            rows.Add((U(96f), r =>
            {
                float w = (r.width - U(20f)) / 3f;
                DMetric(new Rect(r.x, r.y, w, r.height), "PADS", pads.ToString(), $"{treating} treating aircraft");
                DMetric(new Rect(r.x + w + U(10f), r.y, w, r.height), "TRUCKS", trucks.ToString(), "Assigned to pads");
                DMetric(new Rect(r.x + 2f * (w + U(10f)), r.y, w, r.height), "FLUID", $"{stock / 1000f:0.#}k L", $"of {capacity / 1000f:0.#}k L capacity");
            }));
            if (pads == 0) DText(rows, "Build a deicing pad, add a supply area and place truck parking. Then order a truck in Fleet & pads.", width);
            if (area != null)
            {
                bool access = service.HasDeliveryAccess(area);
                DStatus(rows, access ? "Outside road connected" : "Connect this area to outside access", access ? DevGui.Success : DevGui.Accent);
            }
            DHeading(rows, "Operating account", $"{(service.Accounts.FreeSpending ? "Sandbox spending" : "Career budget")} · all expenses are recorded");
            var accounts = service.Accounts;
            rows.Add((U(118f), r =>
            {
                DevGui.SurfaceCard(r);
                GUI.Label(new Rect(r.x + U(14f), r.y + U(10f), r.width - U(28f), U(18f)), "AVAILABLE FUNDS", DevGui.Caption);
                GUI.Label(new Rect(r.x + U(14f), r.y + U(30f), r.width - U(28f), U(28f)), $"${accounts.AvailableFunds:N0}", DevGui.Title);
                float w = (r.width - U(38f)) / 2f;
                if (accounts.sharedBalance != null) { GUI.Label(new Rect(r.x + U(14f), r.yMax - U(42f), r.width - U(28f), U(26f)), "Uses your airport funds", DevGui.Small); return; }
                if (DevGui.Button(new Rect(r.x + U(14f), r.yMax - U(46f), w, U(32f)), accounts.limitedBudget ? "Budget on" : "Use budget", on: accounts.limitedBudget))
                    accounts.limitedBudget = !accounts.limitedBudget;
                if (DevGui.Button(new Rect(r.x + U(24f) + w, r.yMax - U(46f), w, U(32f)), "+ $100,000", "Add sandbox deicing funds")) accounts.AddFunding(100000d);
            }));
            rows.Add((U(94f), r =>
            {
                DevGui.SurfaceCard(r);
                float col = (r.width - U(28f)) / 2f;
                DValue(new Rect(r.x + U(14f), r.y + U(10f), col, U(32f)), "Equipment", $"${accounts.capitalSpent:N0}");
                DValue(new Rect(r.x + U(14f) + col, r.y + U(10f), col, U(32f)), "Fluid & delivery", $"${accounts.fluidSpent:N0}");
                DValue(new Rect(r.x + U(14f), r.y + U(50f), col, U(32f)), "Crew wages", $"${accounts.payrollSpent:0.00}");
                DValue(new Rect(r.x + U(14f) + col, r.y + U(50f), col, U(32f)), "Running", $"${accounts.runningSpent:0.00}");
            }));
        }

        void DeicingFleetRows(List<(float, Action<Rect>)> rows, AirportDeicing service, DeicingWorkArea area, float width)
        {
            DHeading(rows, "Fleet & pad assignments", "Each truck needs parking and a supply area");
            int parking = 0, freeParking = 0;
            foreach (var bay in service.ParkingBays)
            {
                if (bay == null || area != null && bay.supplyAreaId != 0 && bay.supplyAreaId != area.id) continue;
                parking++;
                if (service.TruckFor(bay) == null) freeParking++;
            }
            rows.Add((U(94f), r =>
            {
                DevGui.SurfaceCard(r);
                float x = r.x + U(14f), w = r.width - U(28f);
                GUI.Label(new Rect(x, r.y + U(10f), w - U(152f), U(18f)), "TRUCK PARKING", DevGui.Caption);
                GUI.Label(new Rect(x, r.y + U(32f), w - U(152f), U(24f)), $"{freeParking} free / {parking} bay{(parking == 1 ? "" : "s")}", DevGui.Bold);
                GUI.Label(new Rect(x, r.y + U(63f), w, U(18f)), "One bay per truck. Add parking before expanding the fleet.", DevGui.Small);
                if (DevGui.PrimaryButton(new Rect(r.xMax - U(154f), r.y + U(19f), U(140f), U(34f)), "+ Parking bay", "Place a truck parking bay on a paved apron"))
                {
                    CloseDeicingManagement();
                    airfield.BeginDeicingParking();
                }
            }));
            bool any = false;
            foreach (var stand in airfield.airport.Taxiways.stands)
            {
                if (!stand.DeIcing) continue;
                var facility = service.For(stand);
                if (area != null && service.WorkAreaFor(facility) != area && stand.id != deicingManagementPad) continue;
                any = true;
                var pad = stand;
                var f = facility;
                var supply = service.WorkAreaFor(f);
                var job = service.JobFor(f);
                int machines = f?.machines ?? 0;
                int availableBays = 0;
                foreach (var bay in service.ParkingBays)
                    if (bay != null && service.TruckFor(bay) == null && (bay.standId == 0 || bay.standId == pad.id)) availableBays++;
                rows.Add((U(178f), r =>
                {
                    DevGui.SurfaceCard(r);
                    float x = r.x + U(14f), w = r.width - U(28f);
                    GUI.Label(new Rect(x, r.y + U(10f), w - U(104f), U(22f)), pad.Name, DevGui.Bold);
                    if (DevGui.Button(new Rect(r.xMax - U(104f), r.y + U(9f), U(90f), U(26f)), "Locate"))
                    {
                        airfield.SelectStand(pad);
                        deicingManagementOpen = false;
                    }
                    GUI.Label(new Rect(x, r.y + U(39f), w, U(18f)), $"{machines} / {AirportDeicing.MaxMachines} trucks · {(f?.crewOnDuty == false ? "Crew off shift" : "Crew on shift")}", DevGui.Small);
                    string status = job != null && !job.Completed ? job.Waiting ?? (job.IsSpraying ? "Treating aircraft" : "Trucks positioning")
                        : supply == null ? "Choose a supply area" : machines == 0 ? "Order a truck to open this pad" : f?.crewOnDuty == false ? "Crew off shift" : "Ready";
                    DevGui.StatusPill(new Rect(x, r.y + U(64f), w, U(23f)), status, job?.Waiting != null || supply == null || machines == 0 ? DevGui.Accent : DevGui.Success);
                    if (DevGui.Button(new Rect(x, r.y + U(96f), w, U(30f)), supply != null ? $"Supply: {supply.Name}  ›" : "Assign a supply area  ›", "Choose the refill area for this pad", on: deicingAssigningPad == pad.id))
                        deicingAssigningPad = deicingAssigningPad == pad.id ? 0 : pad.id;
                    float bw = (w - U(8f)) / 2f;
                    bool busy = job != null && !job.Completed;
                    if (DevGui.PrimaryButton(new Rect(x, r.yMax - U(42f), bw, U(30f)), "+ Order truck", $"${AirportDeicing.TruckPrice:N0} including an operator; choose a parking bay",
                        machines < AirportDeicing.MaxMachines && !busy && availableBays > 0 && supply != null && service.Accounts.CanAfford(AirportDeicing.TruckPrice)))
                    {
                        deicingOrderingPad = deicingOrderingPad == pad.id ? 0 : pad.id;
                        deicingAssigningPad = 0;
                    }
                    if (DevGui.Button(new Rect(x + bw + U(8f), r.yMax - U(42f), bw, U(30f)), f?.crewOnDuty == false ? "Start shift" : "End shift",
                        $"${AirportDeicing.OperatorHourlyWage:N0} per operator / game hour", enabled: machines > 0 && (f?.crewOnDuty == false || service.CanEndShift(f))))
                        deicingManagementNote = service.SetCrewOnDuty(f, !f.crewOnDuty, out var problem) ? null : problem;
                }));
                if (machines < AirportDeicing.MaxMachines && availableBays == 0)
                    DText(rows, "Add a parking bay before ordering another truck.", width);
                if (deicingOrderingPad == pad.id)
                {
                    DText(rows, $"Choose parking for the new truck · ${AirportDeicing.TruckPrice:N0}. It will drive in from outside and park here.", width);
                    foreach (var bay in service.ParkingBays)
                    {
                        if (bay == null || service.TruckFor(bay) != null || bay.standId != 0 && bay.standId != pad.id) continue;
                        var selectedBay = bay;
                        rows.Add((U(36f), r =>
                        {
                            if (DevGui.Button(r, $"Order to {selectedBay.Name}", "Purchase one truck and reserve this parking bay"))
                            {
                                deicingManagementNote = service.OrderTruck(DeicingEquipment(pad), selectedBay, out var problem)
                                    ? $"Truck ordered for {pad.Name}. Driving to {selectedBay.Name}." : problem;
                                deicingOrderingPad = 0;
                                deicingManagementScroll = Vector2.zero;
                            }
                        }));
                    }
                }
                if (deicingAssigningPad == pad.id)
                {
                    if (service.WorkAreas.Count == 0) DText(rows, "Build a supply area first using + Supply area.", width);
                    foreach (var item in service.WorkAreas)
                    {
                        if (item == null) continue;
                        var candidate = item;
                        rows.Add((U(34f), r =>
                        {
                            if (DevGui.Button(r, $"{candidate.Name} · {candidate.fluidLitres:N0} L", "Assign this supply area to the pad", on: supply == candidate))
                            {
                                deicingManagementNote = service.AssignWorkArea(DeicingEquipment(pad), candidate)
                                    ? $"{pad.Name} now uses {candidate.Name}." : "Wait for trucks to finish refilling before changing their supply area.";
                                deicingAssigningPad = 0;
                                deicingManagementScroll = Vector2.zero;
                            }
                        }));
                    }
                }
                if (f == null) continue;
                foreach (var truck in service.TrucksFor(f))
                {
                    var vehicle = truck;
                    bool canAssignParking = service.CanAssignParkingBay(vehicle, out var parkingProblem);
                    rows.Add((U(120f), r =>
                    {
                        float x = r.x + U(14f), w = r.width - U(28f);
                        GUI.Label(new Rect(x, r.y, w, U(20f)), $"Truck {vehicle.index + 1} · {vehicle.Status}", DevGui.Label);
                        var home = service.ParkingBayFor(vehicle);
                        GUI.Label(new Rect(x, r.y + U(23f), w, U(18f)), home != null ? home.Name : "Original home · no placed bay", DevGui.Small);
                        GUI.Label(new Rect(x, r.y + U(45f), w, U(18f)), $"{vehicle.FluidLitres:N0} / {vehicle.CapacityLitres:N0} L onboard", DevGui.Small);
                        DevGui.ProgressBar(new Rect(x, r.y + U(70f), w, U(5f)), vehicle.FluidLitres / vehicle.CapacityLitres, DevGui.Primary);
                        if (DevGui.Button(new Rect(x, r.y + U(85f), w, U(30f)), home == null ? "Assign parking bay" : "Change parking bay",
                            parkingProblem ?? "Choose a placed bay. The idle truck drives to its new home.", on: deicingParkingTruck == vehicle, enabled: canAssignParking))
                            deicingParkingTruck = deicingParkingTruck == vehicle ? null : vehicle;
                    }));
                    if (deicingParkingTruck == vehicle)
                    {
                        bool free = false;
                        foreach (var bay in service.ParkingBays)
                        {
                            if (bay == null || service.TruckFor(bay) != null) continue;
                            free = true;
                            var candidate = bay;
                            rows.Add((U(34f), r =>
                            {
                                if (DevGui.Button(r, $"Assign {candidate.Name}", "The truck will drive to this bay without changing its fluid stock"))
                                {
                                    deicingManagementNote = service.AssignParkingBay(vehicle, candidate, out var problem)
                                        ? $"Truck {vehicle.index + 1} is driving to {candidate.Name}." : problem;
                                    deicingParkingTruck = null;
                                    deicingManagementScroll = Vector2.zero;
                                }
                            }));
                        }
                        if (!free) DText(rows, "No free parking bays. Use + Parking bay to place one first.", width);
                    }
                }
            }
            if (!any) DText(rows, area == null ? "No deicing pads yet. Build a pad from Deicing › Pads." : "No pads assigned here. Select All airport areas to assign a pad to this supply area.", width);
        }

        void DeicingSupplyRows(List<(float, Action<Rect>)> rows, AirportDeicing service, DeicingWorkArea area, float width)
        {
            if (area == null)
            {
                DHeading(rows, "Supply areas", "Choose an area to manage deliveries and recycling");
                foreach (var item in service.WorkAreas)
                {
                    if (item == null) continue;
                    var candidate = item;
                    rows.Add((U(92f), r =>
                    {
                        DevGui.SurfaceCard(r);
                        GUI.Label(new Rect(r.x + U(14f), r.y + U(10f), r.width - U(120f), U(22f)), candidate.Name, DevGui.Bold);
                        GUI.Label(new Rect(r.x + U(14f), r.y + U(38f), r.width - U(120f), U(18f)), $"{candidate.fluidLitres:N0} / {candidate.capacityLitres:N0} L", DevGui.Small);
                        DevGui.ProgressBar(new Rect(r.x + U(14f), r.yMax - U(18f), r.width - U(28f), U(5f)), candidate.fluidLitres / candidate.capacityLitres, DevGui.Primary);
                        if (DevGui.Button(new Rect(r.xMax - U(100f), r.y + U(18f), U(86f), U(32f)), "Manage")) SelectDeicingManagementArea(candidate.id);
                    }));
                }
                if (service.WorkAreas.Count == 0) DText(rows, "Place a supply area on a paved apron. Its storage can serve several deicing pads.", width);
                return;
            }
            int assignedPads = DeicingAssignedPadCount(service, area);
            DHeading(rows, area.Name, $"{assignedPads} pad{(assignedPads == 1 ? " shares" : "s share")} this storage");
            bool access = service.HasDeliveryAccess(area);
            DStatus(rows, access ? "Outside road connected" : "Outside road required for deliveries", access ? DevGui.Success : DevGui.Accent);
            rows.Add((U(132f), r =>
            {
                DevGui.SurfaceCard(r);
                float x = r.x + U(14f), w = r.width - U(28f);
                GUI.Label(new Rect(x, r.y + U(10f), w, U(18f)), "FLUID STORAGE", DevGui.Caption);
                GUI.Label(new Rect(x, r.y + U(33f), w, U(28f)), $"{area.fluidLitres:N0} / {area.capacityLitres:N0} L", DevGui.Title);
                DevGui.ProgressBar(new Rect(x, r.y + U(71f), w, U(8f)), area.fluidLitres / area.capacityLitres, DevGui.Primary);
                GUI.Label(new Rect(x, r.y + U(94f), w, U(20f)), area.DeliveryPending ? DeicingDeliveryStatus(service, area) : "No delivery pending", DevGui.Small);
            }));
            float capacity = service.OrderCapacity(area), batch = Mathf.Min(5000f, capacity);
            rows.Add((U(38f), r =>
            {
                float w = (r.width - U(10f)) / 2f;
                if (DevGui.PrimaryButton(new Rect(r.x, r.y, w, r.height), $"Order {batch:N0} L", $"${AirportDeicing.DeliveryPrice(batch):N0}",
                    access && !area.DeliveryPending && batch >= 1f && service.Accounts.CanAfford(AirportDeicing.DeliveryPrice(batch)))) OrderDeicingFluid(area, batch);
                if (DevGui.Button(new Rect(r.x + w + U(10f), r.y, w, r.height), "Fill storage", $"Order {capacity:N0} L · ${AirportDeicing.DeliveryPrice(capacity):N0}",
                    enabled: access && !area.DeliveryPending && capacity >= 1f && service.Accounts.CanAfford(AirportDeicing.DeliveryPrice(capacity)))) OrderDeicingFluid(area, capacity);
            }));
            if (!access) DText(rows, "Connect a service road from this area's entrance to an outside access point. The tanker needs a clear paved lane beside the tank.", width);
            DHeading(rows, "Recycling", "Automatic underground collection from assigned pads");
            if (!area.recyclingInstalled)
            {
                rows.Add((U(142f), r =>
                {
                    DevGui.SurfaceCard(r);
                    float x = r.x + U(14f), w = r.width - U(28f);
                    GUI.Label(new Rect(x, r.y + U(12f), w, U(26f)), "Recover up to 60% of used fluid", DevGui.Bold);
                    DWrapped(new Rect(x, r.y + U(43f), w, U(40f)), "Returns recovered fluid to storage automatically. Every aircraft still receives full wing treatment.");
                    if (DevGui.PrimaryButton(new Rect(x, r.yMax - U(46f), w, U(32f)), $"Install recycling · ${AirportDeicing.RecyclingPrice:N0}",
                        "Includes collection drains, hidden connections and a filter skid", !service.InUse(area) && service.Accounts.CanAfford(AirportDeicing.RecyclingPrice))) BuyDeicingRecycling(area);
                }));
            }
            else
            {
                rows.Add((U(100f), r =>
                {
                    DevGui.SurfaceCard(r);
                    float col = (r.width - U(28f)) / 2f;
                    DValue(new Rect(r.x + U(14f), r.y + U(12f), col, U(35f)), "Recovered", $"{area.recycledLitres:N0} L");
                    DValue(new Rect(r.x + U(14f) + col, r.y + U(12f), col, U(35f)), "Fluid savings", $"${area.recycledLitres * AirportDeicing.FluidPricePerLitre:N0}");
                    GUI.Label(new Rect(r.x + U(14f), r.y + U(65f), r.width - U(28f), U(20f)), area.runoffLitres <= .001f ? "Ready to collect runoff" : $"{area.runoffLitres:N0} L processing · returns automatically", DevGui.Small);
                }));
            }
            rows.Add((U(34f), r =>
            {
                if (DevGui.Button(r, "Locate supply area")) { airfield.SelectDeicingWorkArea(area); deicingManagementOpen = false; }
            }));
        }

        static void DMetric(Rect r, string label, string value, string sub)
        {
            DevGui.SurfaceCard(r);
            GUI.Label(new Rect(r.x + U(12f), r.y + U(10f), r.width - U(24f), U(16f)), label, DevGui.Caption);
            GUI.Label(new Rect(r.x + U(12f), r.y + U(30f), r.width - U(24f), U(28f)), value, DevGui.Title);
            GUI.Label(new Rect(r.x + U(12f), r.y + U(66f), r.width - U(24f), U(18f)), sub, DevGui.Small);
        }

        static void DValue(Rect r, string label, string value)
        {
            GUI.Label(new Rect(r.x, r.y, r.width, U(16f)), label, DevGui.Small);
            GUI.Label(new Rect(r.x, r.y + U(18f), r.width, U(20f)), value, DevGui.Bold);
        }

        static void DHeading(List<(float, Action<Rect>)> rows, string heading, string sub)
        {
            rows.Add((U(48f), r =>
            {
                GUI.Label(new Rect(r.x, r.y, r.width, U(24f)), heading, DevGui.Bold);
                GUI.Label(new Rect(r.x, r.y + U(26f), r.width, U(18f)), sub, DevGui.Small);
            }));
        }

        static void DStatus(List<(float, Action<Rect>)> rows, string text, Color tint) =>
            rows.Add((U(28f), r => DevGui.StatusPill(r, text, tint)));

        static void DText(List<(float, Action<Rect>)> rows, string text, float width, bool notice = false)
        {
            var style = new GUIStyle(DevGui.Label) { wordWrap = true, alignment = TextAnchor.UpperLeft };
            float h = style.CalcHeight(new GUIContent(text), width - U(24f));
            rows.Add((h + U(24f), r =>
            {
                if (notice) DevGui.SurfaceCard(r);
                GUI.Label(new Rect(r.x + U(12f), r.y + U(12f), r.width - U(24f), h), text, style);
            }));
        }

        static void DWrapped(Rect r, string text) => GUI.Label(r, text, new GUIStyle(DevGui.Small) { wordWrap = true, alignment = TextAnchor.UpperLeft });
    }
}
