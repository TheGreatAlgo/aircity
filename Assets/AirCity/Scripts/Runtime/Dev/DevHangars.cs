using System;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Game;
using UnityEngine;

namespace AirCity.Dev
{
    public partial class DevAirfieldTool
    {
        string hangarProblem;
        public string HangarProblem => hangarProblem;
        public bool HangarSnapped { get; private set; }
        public HangarSize SelectedHangarSize { get; private set; }

        public void SetHangarSize(HangarSize size)
        {
            SelectedHangarSize = size;
            hangarProblem = null;
            HangarSnapped = false;
        }

        (Vector2 door, float heading, bool snapped) HangarPlacement(Vector3 world)
        {
            var cursor = Geo.XZ(airport.transform.InverseTransformPoint(world));
            var result = (door: cursor, heading: objectHeading, snapped: false);
            if (!placementSnap) return result;
            var spec = HangarSpec.For(SelectedHangarSize);
            float margin = SelectedHangarSize == HangarSize.Small ? 7f : spec.ConnectionHalfSpan;
            float best = cam != null ? Mathf.Clamp(SnapReach(world), 4f, 8f) : 4f;
            var net = airport.Taxiways;
            foreach (var edge in net.edges)
            {
                var a = net.Node(edge.a); var b = net.Node(edge.b);
                if (!HangarConnection.IsLevelTaxiway(edge, a, b) ||
                    edge.width + .01f < RunwayRules.TaxiwayWidthFor(spec.TaxiCode) ||
                    spec.TaxiCode != 'A' && !edge.surface.IsPaved()) continue;
                var start = Geo.XZ(a.position);
                var line = Geo.XZ(b.position) - start;
                float length = line.magnitude;
                if (length < 2f * margin) continue;
                var along = line / length;
                float at = Vector2.Dot(cursor - start, along);
                if (at < 0f || at > length) continue;
                var centre = start + along * Mathf.Clamp(at, margin, length - margin);
                var axis = Geo.Right(along);
                if (Vector2.Dot(centre - cursor, axis) < 0f) axis = -axis;
                var door = centre - axis * (edge.width * .5f + spec.Setback);
                float distance = Vector2.Distance(cursor, door);
                if (distance >= best) continue;
                best = distance;
                result = (door, Bearing(axis), true);
            }
            return result;
        }

        void HangarTool(bool click, bool free, Vector3 world)
        {
            if (!free) { HangarSnapped = false; return; }
            var draft = HangarPlacement(world);
            HangarSnapped = draft.snapped;
            if (click) BuildHangar(draft);
            else hangarProblem = HangarConstruction.Problem(game, airport, draft.door, draft.heading, SelectedHangarSize);
        }

        /// <summary>Commit the same hangar draft shown by the construction tool.</summary>
        public bool PlaceHangar(Vector3 world) => airport != null && BuildHangar(HangarPlacement(world));

        bool BuildHangar((Vector2 door, float heading, bool snapped) draft)
        {
            if (!HangarConstruction.TryBuild(game,airport,draft.door,draft.heading,out var made,out hangarProblem,SelectedHangarSize))
            { game?.SetMessage(hangarProblem); return false; }
            SelectHangar(made);
            return true;
        }

        public void SelectHangar(SmallHangar hangar)
        {
            SetTool(DevTool.Select);
            hangarProblem = null;
            Select(hangar == null ? Pick.None : new Pick {kind=Kind.Hangar,hangar=hangar,apron=-1});
        }

        void DrawHangarPreview(bool pointing,Vector3 world)
        {
            if (!pointing || tool != DevTool.Hangar) return;
            var (door, heading, snapped) = HangarPlacement(world);
            var spec = HangarSpec.For(SelectedHangarSize);
            loop.Clear();
            foreach (var p in spec.Footprint(door,heading)) loop.Add(airport.transform.TransformPoint(Geo.X0Z(p)));
            var color = hangarProblem == null ? new Color(.25f,.85f,.72f) : DevGui.Danger;
            outlines.Loop(cam,loop,color,3);
            var axis = FromBearing(heading); var right = Geo.Right(axis);
            float reach = snapped || SelectedHangarSize != HangarSize.Small ? spec.Setback : spec.MaxSetback;
            float half = spec.ApproachWidth * .5f;
            loop.Clear();
            foreach (var p in new[] {door-right*half,door+right*half,door+right*half+axis*reach,door-right*half+axis*reach})
                loop.Add(airport.transform.TransformPoint(Geo.X0Z(p)));
            outlines.Loop(cam,loop,new Color(.35f,.7f,.85f),snapped ? 3 : 1);
        }

        bool TrySnapHangarTaxiway(TaxiwayNetwork net, Vector2 cursor, float reach, out LineSnap snap)
        {
            snap = default;
            float best = Mathf.Clamp(reach, 4f, 8f);
            foreach (var hangar in airport.HangarBuildings)
            {
                var spec = hangar.Spec;
                if (NewWidth + .01f < RunwayRules.TaxiwayWidthFor(spec.TaxiCode) ||
                    spec.TaxiCode != 'A' && !surface.IsPaved()) continue;
                var axis = hangar.Axis;
                var along = Geo.Right(axis);
                var centre = HangarTaxiwayCentre(hangar);
                float side = Vector2.Dot(cursor - centre, along);
                // The door needs turning room on both sides of the centreline crossing.
                float frontage = spec.ConnectionHalfSpan;
                var point = centre + along * (Mathf.Clamp(Mathf.Abs(side), frontage, 2f * frontage) * (side < 0f ? -1f : 1f));
                float distance = Vector2.Distance(point, cursor);
                if (distance >= best || chainNode >= 0 && Vector2.Distance(net.At(chainNode), point) < 6f) continue;
                if (HangarConnection.Approach(hangar, out _)) continue;
                best = distance;
                snap = new LineSnap { p = point, node = -1, hangarFrontage = hangar, what = hangar.name + "'s taxiway alignment" };
            }
            return snap.hangarFrontage != null;
        }

        Vector2 HangarTaxiwayCentre(SmallHangar hangar) =>
            hangar.Door + hangar.Axis * (hangar.Spec.Setback + NewWidth * .5f);

        void DrawHangarTaxiwayGuide(SmallHangar hangar)
        {
            var centre = HangarTaxiwayCentre(hangar);
            var span = Geo.Right(hangar.Axis) * (2f * hangar.Spec.ConnectionHalfSpan);
            loop.Clear();
            loop.Add(airport.transform.TransformPoint(Geo.X0Z(centre - span, .05f)));
            loop.Add(airport.transform.TransformPoint(Geo.X0Z(centre + span, .05f)));
            outlines.Line(cam, loop, new Color(.35f,.7f,.85f), 2f);
        }
    }

    public partial class DevToolbar
    {
        const string HangarFocus = "hangar-lease-";
        enum HangarPage { Overview, Aircraft, Building }
        static readonly AirfieldWorkspaceGui.Tab[] HangarTabs =
        {
            new AirfieldWorkspaceGui.Tab("Overview", "overview"),
            new AirfieldWorkspaceGui.Tab("Aircraft", "stand"),
            new AirfieldWorkspaceGui.Tab("Building", "hangar")
        };
        SmallHangar hangarSubject;
        string hangarTenant = "Local aircraft owner", hangarRegistration = "AC-001", hangarMessage;
        int hangarTypeIndex;
        bool hangarCapturedKeyboard;
        int aircraftHangarIndex;
        HangarPage hangarPage = HangarPage.Aircraft;
        Vector2 hangarScroll;
        Rect hangarBody;

        void AircraftHangar(AirCity.Aircraft.Aircraft aircraft)
        {
            if (traffic==null || airfield?.airport==null) return;
            var owner=SmallHangar.OfResident(aircraft);
            if (owner!=null && owner.ResidentInBay)
            {
                Buttons(B("Taxi to stand",()=> { if (!owner.TaxiToStand(out hangarMessage)) airfield.game?.SetMessage(hangarMessage); else HangarChanged(); },"Push out of the hangar, then taxi to a free aircraft stand"));
                if (hangarMessage!=null) Danger(hangarMessage);
                return;
            }
            var bays=airfield.airport.Hangars.Where(h=>(owner != null ? h == owner : !h.Occupied) &&
                h.DoorsOpen && h.CanStore(aircraft.type) && h.AccessFor(aircraft.type)==null).ToArray();
            if (bays.Length==0) return;
            aircraftHangarIndex=(aircraftHangarIndex%bays.Length+bays.Length)%bays.Length;
            var target=bays[aircraftHangarIndex];
            if (bays.Length>1) PropStepper("Hangar",target.BayName,()=>aircraftHangarIndex--,()=>aircraftHangarIndex++,"Choose an available connected bay");
            Buttons(B("Taxi to hangar",()=> { traffic.TaxiToHangar(target); if (traffic.Message==null) HangarChanged(); },"Taxi through the connected taxiway network into "+target.BayName+" and lease the bay"));
            if (hangarMessage!=null) Danger(hangarMessage);
        }

        void HangarBuildInspector()
        {
            var size = airfield.SelectedHangarSize;
            var spec = HangarSpec.For(size);
            Title(spec.Name);
            PropStepper("Size", size.ToString(), () => airfield.SetHangarSize(size - 1), () => airfield.SetHangarSize(size + 1),
                "Choose a light-aircraft, executive or airline hangar", size > HangarSize.Small, size < HangarSize.Airline);
            string capacity = size == HangarSize.Executive ? "3 indoor parking spots · 1 shared door" : size == HangarSize.Airline ? "1 widebody bay" : "1 light aircraft bay";
            Note($"{spec.Width:0} × {spec.Depth:0} m · {capacity}");
            PropText("Build",airfield.game == null || airfield.game.Sandbox ? "Free in Sandbox" : $"${spec.Price:N0}");
            PropText("Rent",$"${spec.Rent:N0} per bay / 30 days");
            if (size != HangarSize.Small)
                PropText("Taxiway", $"Paved code {spec.TaxiCode}+ · {spec.Setback:0} m setback");
            Note(airfield.HangarSnapped ? "Doorway aligned with the taxiway." :
                "Move the doorway near a taxiway to align it. To build a new taxiway, snap across the front of the hangar.");
            Note("Prepare cleared, level ground. Select a bay to lease it, or use Taxi to hangar on a compatible parked aircraft.");
            if (airfield.HangarProblem != null) Danger(airfield.HangarProblem);
            Buttons(B("Cancel",()=>airfield.SetTool(DevTool.Select),hint:"Esc"));
        }

        void DrawHangarWorkspace(SmallHangar hangar, Rect panel)
        {
            if (hangarSubject != hangar)
            {
                hangarSubject = hangar; hangarMessage = null; hangarTypeIndex = 0; hangarScroll = Vector2.zero;
                GUI.FocusControl(null);
                hangarTenant = hangar.Size == HangarSize.Airline ? "Airline operator" : "Local aircraft owner";
                int serial = 1;
                while (airfield.airport.Hangars.Any(h=>h.Occupied && h.Registration=="AC-"+serial.ToString("000"))) serial++;
                hangarRegistration = "AC-" + serial.ToString("000");
            }
            var layout = new AirfieldWorkspaceGui.Layout(panel);
            hangarBody = layout.Body;
            AirfieldWorkspaceGui.Frame(layout, hangar.Spec.Name, hangar.BuildingRoot.name, "hangar", () =>
            {
                GUI.FocusControl(null);
                airfield.SelectHangar(null);
            });
            AirfieldWorkspaceGui.Navigation(layout, HangarTabs, (int)hangarPage, next =>
            {
                GUI.FocusControl(null);
                hangarPage = (HangarPage)next;
                hangarScroll = Vector2.zero;
            });

            string access = hangar.AccessProblem;
            Btn? action = null;
            DrawWorkspaceRows(layout.Body, ref hangarScroll, () =>
            {
                switch (hangarPage)
                {
                    case HangarPage.Overview: HangarOverview(hangar, access); break;
                    case HangarPage.Aircraft: action = HangarAircraft(hangar, access); break;
                    case HangarPage.Building: action = HangarBuilding(hangar); break;
                }
            });

            var footer = layout.Footer;
            var button = new Rect(footer.xMax - U(158), footer.y + U(9), U(144), U(32));
            if (action is Btn chosen && DevGui.PrimaryButton(button, chosen.label, chosen.tip, chosen.enabled))
                chosen.act();
            string status = hangarMessage ?? airfield.HangarProblem ?? access ?? "Taxiway connected";
            float end = action.HasValue ? button.x - U(8) : footer.xMax - Pad;
            GUI.Label(new Rect(footer.x + Pad, footer.y + U(4), end - footer.x - Pad, footer.height - U(8)),
                new GUIContent(status, status), Wrapped);
        }

        void HangarOverview(SmallHangar hangar, string access)
        {
            Section("STORAGE");
            var bays = hangar.Bays.ToArray();
            PropText("Storage", $"{bays.Count(bay => bay.Occupied)} / {bays.Length} occupied");
            PropText("Rent", $"${hangar.Spec.Rent:N0} / 30 days per bay");
            PropText("Earned", $"${hangar.RentEarned:N0}");
            if (airfield.game == null || airfield.game.Sandbox) Note("Sandbox. Finances are paused.");
            Section("TAXIWAY ACCESS");
            if (access != null) Danger(access);
            else Note("Taxiway connected. Paved approach ready.");
        }

        Btn? HangarAircraft(SmallHangar hangar, string access)
        {
            Section("RESIDENT AIRCRAFT");
            var bays = hangar.Bays.OrderBy(bay => bay.BayNumber).ToArray();
            if (bays.Length > 1)
            {
                int index = Array.IndexOf(bays, hangar);
                PropStepper("Spot", $"{hangar.BayNumber} · {(hangar.Occupied ? "Leased" : "Vacant")}",
                    () => airfield.SelectHangar(bays[index - 1]), () => airfield.SelectHangar(bays[index + 1]),
                    "Select a parking spot and its lease", index > 0, index < bays.Length - 1);
            }
            if (hangar.Occupied)
            {
                PropText("Tenant",hangar.TenantName);
                PropText("Aircraft",hangar.ResidentType.TypeName);
                PropText("Tail",hangar.Registration);
                var flight=hangar.ResidentAircraft!=null ? airfield.airport.Traffic.FlightOf(hangar.ResidentAircraft) : null;
                PropText("Location",flight!=null ? flight.Status : hangar.ResidentInBay ? "Stored in bay" : hangar.ResidentPresent ? "Outside on airport" : "Off airport");
                Buttons(B("End lease",()=> { hangar.EndLease(); HangarChanged(); },"Return the resident to its bay before ending the lease",enabled:hangar.CanEndLease,danger:true));
                if (!hangar.ResidentPresent) return null;
                return B(hangar.ResidentInBay ? "Taxi to stand" : "Taxi into hangar", () =>
                {
                    bool moved = hangar.ResidentInBay ? hangar.TaxiToStand(out hangarMessage) : hangar.TaxiIntoHangar(out hangarMessage);
                    if (moved) HangarChanged();
                }, "Use the connected taxiways. Aircraft taxi at walking pace through the doorway.",
                    enabled: !hangar.ResidentTravelling && access == null);
            }

            var types = airfield.airport.aircraftTypes.Where(hangar.CanStore).Distinct().ToArray();
            if (types.Length == 0)
            {
                Note("No compatible aircraft are available.");
                return null;
            }
            hangarTypeIndex = (hangarTypeIndex % types.Length + types.Length) % types.Length;
            var type = types[hangarTypeIndex];
            string typeAccess = access ?? hangar.AccessFor(type);
            PropStepper("Aircraft", type.TypeName, () => hangarTypeIndex--, () => hangarTypeIndex++, "Choose the resident aircraft type",
                types.Length > 1, types.Length > 1);
            HangarText("Tenant", "tenant", () => hangarTenant, v => hangarTenant = v, 48);
            HangarText("Tail", "tail", () => hangarRegistration, v => hangarRegistration = v, 10);
            if (access == null && typeAccess != null) Danger(typeAccess);
            return B("Start lease", () =>
            {
                if (hangar.TryLease(type, hangarTenant, hangarRegistration, out hangarMessage)) HangarChanged();
            }, "Reserve this bay for the named operator's aircraft", enabled: typeAccess == null);
        }

        Btn HangarBuilding(SmallHangar hangar)
        {
            Section("BUILDING");
            string door = hangar.Size == HangarSize.Small ? "doors" : "door";
            PropText(hangar.Size == HangarSize.Small ? "Doors" : "Door", hangar.DoorsOpen ? "Open" : "Closed");
            Buttons(B("Build another",()=> { airfield.SetHangarSize(hangar.Size); airfield.SetTool(DevTool.Hangar); },"Place another hangar of this size"));
            Buttons(B("Demolish hangar",()=>airfield.DeleteSelection(),"End every bay lease before demolition",enabled:hangar.CanDemolish,danger:true));
            return B((hangar.DoorsOpen ? "Close " : "Open ") + door, () => { hangar.SetDoorsOpen(!hangar.DoorsOpen); HangarChanged(); },
                "The doorway stays clear while an aircraft is moving", enabled: !hangar.DoorsOpen || hangar.CanCloseDoors);
        }

        void HangarChanged()
        {
            hangarMessage = null;
            GUI.FocusControl(null);
            airfield.game?.SharedConstructionChanged();
        }

        void HangarText(string label,string key,Func<string> read,Action<string> write,int limit)
        {
            Prop(label,r=>
            {
                GateDesignerGui.Begin();
                DevGui.Fill(r,DevGui.Surface,U(4));
                GUI.SetNextControlName(HangarFocus+key);
                write(GUI.TextField(r,read(),limit,GateDesignerGui.Input));
            });
        }

        void UpdateHangarKeyboardFocus()
        {
            var focused = GUI.GetNameOfFocusedControl();
            bool ours = focused != null && focused.StartsWith(HangarFocus,StringComparison.Ordinal);
            bool showing = hangarPage == HangarPage.Aircraft && !DeicingManagementOpen &&
                Subject() is SmallHangar hangar && !hangar.Occupied;
            if (ours && (!showing || Event.current.type == EventType.MouseDown && !hangarBody.Contains(Event.current.mousePosition) ||
                Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.Escape)))
            { GUI.FocusControl(null); ours = false; }
            if (ours) { DevGui.KeyboardCaptured = true; hangarCapturedKeyboard = true; }
            else if (hangarCapturedKeyboard) { DevGui.KeyboardCaptured = false; hangarCapturedKeyboard = false; }
        }
    }
}
