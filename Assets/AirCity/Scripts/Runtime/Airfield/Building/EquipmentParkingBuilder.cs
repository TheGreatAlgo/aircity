namespace AirCity.Airfield.Building
{
    /// <summary>Shared storage courts, legacy parking bays and active baggage working areas.</summary>
    sealed class EquipmentParkingBuilder
    {
        public void Build(StandMarkings.EquipmentBay bay, PaintBuilder paint)
        {
            Outline(bay, paint, 0f, PaintBuilder.White);
            if (!bay.working) Outline(bay, paint, .25f, PaintBuilder.Red);
            if (bay.storageArea)
            {
                float labelHeight=UnityEngine.Mathf.Min(.8f,bay.width/12f);
                paint.Text(bay.Point(0,bay.length*.5f-.7f),bay.facing,"EQUIPMENT "+bay.stands,labelHeight*.49f,PaintBuilder.White,labelHeight);
                return;
            }
            float h = UnityEngine.Mathf.Min(.85f, bay.width / 6f);
            paint.Text(bay.Point(0f, -bay.length * .5f + 1.25f), bay.facing, bay.equipment, h*.49f, PaintBuilder.White, h);
            paint.Text(bay.Point(0f, -bay.length * .5f + 2.5f), bay.facing, "STAND " + bay.stands, h*.49f, PaintBuilder.White, h);
            if (bay.working) return;
            var tip = bay.Point(0f, bay.length*.5f - 1.1f);
            var back = tip - bay.facing * 1.1f;
            paint.Line(back, tip, .075f, PaintBuilder.White);
            var across = Geo.Right(bay.facing) * .4f;
            paint.Line(tip, back + across, .075f, PaintBuilder.White);
            paint.Line(tip, back - across, .075f, PaintBuilder.White);
        }

        static void Outline(StandMarkings.EquipmentBay b, PaintBuilder paint, float inset, UnityEngine.Color color)
        {
            var outline=b.Boundary(inset);
            void Edge(UnityEngine.Vector2 p, UnityEngine.Vector2 q)
            {
                if (b.working) paint.Dashes(p,q,.075f,1f,.65f,color,outsideParking:true);
                else paint.Line(p,q,.075f,color,.075f);
            }
            for (int i=0;i<outline.Length;i++) Edge(outline[i],outline[(i+1)%outline.Length]);
        }
    }
}
