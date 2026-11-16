using AirCity.Airfield;
using UnityEngine;

namespace AirCity.Dev
{
    public partial class DevToolbar
    {
        int deicingCatalogTab;
        bool IsDeicingTool => airfield != null && (airfield.Tool == DevTool.DeIcing || airfield.Tool == DevTool.DeicingStorage ||
            airfield.Tool == DevTool.DeicingParking || airfield.Tool == DevTool.DeicingOutsideAccess);

        public void OpenDeicingCatalog(int tab = 0)
        {
            if (airfield == null) airfield = GetComponent<DevAirfieldTool>();
            if (airfield == null) return;
            CloseDeicingManagement();
            gateDesigner.Close(); openPop = null;
            deicingCatalogTab = Mathf.Clamp(tab, 0, 2);
            if (deicingCatalogTab == 1) airfield.BeginDeicingWorkArea();
            else if (deicingCatalogTab == 2) airfield.BeginDeicingParking();
            else airfield.SetTool(DevTool.DeIcing);
        }

        void DeicingCatalog(Rect button, Rect bar)
        {
            float right = Screen.width - U(ColumnUnits) - U(24);
            float width = Mathf.Min(U(520), Mathf.Max(U(340), right - U(12)));
            float height = U(208);
            var panel = new Rect(Mathf.Clamp(button.center.x - width * .5f, U(12), Mathf.Max(U(12), right - width)), bar.y - height - U(8), width, height);
            DevGui.Panel(panel);
            float x = panel.x + U(12), y = panel.y + U(8), contentW = width - U(24);
            GUI.Label(new Rect(x, y, U(180), U(28)), "Deicing", DevGui.Title);
            if (DevGui.PrimaryButton(new Rect(panel.xMax - U(152), y, U(140), U(28)), "Manage airport", "Fleet, parking, pad assignments, fluid orders and recycling"))
                OpenDeicingManagement();
            y += U(34);
            string[] tabs = { "Pads", "Buildings", "Objects" };
            float tabW = (contentW - U(12)) / 3;
            for (int i = 0; i < tabs.Length; i++)
                if (DevGui.Button(new Rect(x + (tabW + U(6)) * i, y, tabW, U(28)), tabs[i], on: deicingCatalogTab == i))
                    OpenDeicingCatalog(i);
            y += U(34);
            if (deicingCatalogTab == 0) DeicingPadCards(new Rect(x, y, contentW, U(84)));
            else if (deicingCatalogTab == 1)
            {
                float cw = (contentW - U(8)) * .5f;
                if (DevGui.Card(new Rect(x, y, cw, U(84)), r => BuildPicture(r, "DeicingTank", "deicing-storage"), "Supply station", "$35,000 · 30,000 L",
                    "Shared fluid storage and refill points. Place on a work apron, then connect an outside service road.", airfield.Tool == DevTool.DeicingStorage))
                    airfield.BeginDeicingWorkArea();
                if (DevGui.Card(new Rect(x + cw + U(8), y, cw, U(84)), r => BuildPicture(r, "DeicingRecycler", "deicing-storage"), "Recycling upgrade", "$45,000 · recover 60%",
                    "Purchase for a supply station in management. Assigned pads connect automatically underground.", false))
                { OpenDeicingManagement(airfield.SelectedDeicingWorkArea); deicingManagementTab = 2; }
            }
            else
            {
                float cw = Mathf.Min(U(180), (contentW - U(16)) / 3);
                if (DevGui.Card(new Rect(x, y, cw, U(84)), r => ParkingPicture(r), "Truck parking", "$1,000 · one truck",
                    "Place parking on a paved work area before ordering a deicing truck.", airfield.Tool == DevTool.DeicingParking))
                    airfield.BeginDeicingParking();
                if (DevGui.Card(new Rect(x + cw + U(8), y, cw, U(84)), r => DevGui.DrawIcon(r, DevGui.Icon("road"), DevGui.Primary), "Outside connection", "Service-road endpoint",
                    "Mark where the service road reaches the outside world. Required for fluid and truck deliveries.", airfield.Tool == DevTool.DeicingOutsideAccess))
                    airfield.BeginDeicingOutsideAccess();
                if (DevGui.Card(new Rect(x + 2 * (cw + U(8)), y, cw, U(84)), r => BuildPicture(r, "DeicingTruck", "depot"), "Deicing truck", "$180,000 · order fleet",
                    "Order from management after placing a parking bay. The truck drives in from the outside connection.", false))
                    OpenDeicingManagement();
            }
            y += U(92);
            if (airfield.SupportsPlacementRotation) PlacementRotationRow(new Rect(x, y, contentW, U(28)));
            else GUI.Label(new Rect(x, y, contentW, U(28)), "Choose a service-road endpoint  •  Esc to cancel", DevGui.Small);
        }

        void DeicingPadCards(Rect row)
        {
            float gap = U(6), cw = (row.width - gap * 5) / 6;
            for (int i = 0; i < SizeCodes.Count; i++)
            {
                var code = (SizeCode)i;
                float span = RunwayRules.MaxWingspan(code.Letter());
                bool on = airfield.Tool == DevTool.DeIcing && airfield.StandCode == code;
                if (DevGui.Card(new Rect(row.x + (cw + gap) * i, row.y, cw, row.height), r => AircraftPicture(r, span / 80f, on),
                    "Pad " + code.Letter(), $"to {span:0} m", CodeTips[i], on))
                { airfield.SetTool(DevTool.DeIcing); airfield.SetStandCode(code); }
            }
        }

        static void BuildPicture(Rect r, string asset, string fallback)
        {
            var texture = Resources.Load<Texture2D>("AirCityBuild/" + asset);
            if (texture != null) GUI.DrawTexture(r, texture, ScaleMode.ScaleToFit, true);
            else DevGui.DrawIcon(r, DevGui.Icon(fallback), DevGui.Primary);
        }

        static void ParkingPicture(Rect r)
        {
            var c = DevGui.GUI(DevGui.Primary);
            DevGui.Outline(new Rect(r.x + r.width * .15f, r.y + r.height * .08f, r.width * .7f, r.height * .84f), c, U(4), U(2));
            var style = new GUIStyle(DevGui.Title) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(U(32)) };
            GUI.Label(r, "P", style);
        }

        void PlacementRotationRow(Rect r)
        {
            GUI.Label(new Rect(r.x, r.y, U(46), r.height), "Turn", DevGui.Small);
            if (DevGui.Button(new Rect(r.x + U(46), r.y, U(30), r.height), "−", "Rotate 15° anticlockwise")) airfield.RotatePlacement(-15);
            GUI.Label(new Rect(r.x + U(78), r.y, U(46), r.height), $"{airfield.PlacementHeading:0}°", Centred);
            if (DevGui.Button(new Rect(r.x + U(126), r.y, U(30), r.height), "+", "Rotate 15° clockwise")) airfield.RotatePlacement(15);
            if (DevGui.Button(new Rect(r.x + U(164), r.y, U(100), r.height), airfield.PlacementSnap ? "Snap 15°" : "Free angle",
                "Toggle angle snapping. Z / Shift+Z: ±45°. Shift + wheel: 15° snapped, 5° free; Alt: 1°.", on: airfield.PlacementSnap))
                airfield.PlacementSnap = !airfield.PlacementSnap;
            if (r.width > U(500)) GUI.Label(new Rect(r.x + U(280), r.y, r.width - U(280), r.height), "Z turn  •  Shift + wheel fine turn", DevGui.Small);
            else if (r.width > U(430)) GUI.Label(new Rect(r.x + U(276), r.y, r.width - U(276), r.height), "Z / Shift + wheel", DevGui.Small);
        }
    }
}
