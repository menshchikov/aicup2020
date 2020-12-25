using System.Collections.Generic;
using Aicup2020.Model;

namespace Aicup2020
{
    public class TacticalGroup
    {
        public List<Entity> Units;
        public Vec2Int TargetPosition;
        public Vec2Int CurrentPosition;
        public string Name;
        public StrategicPoint point;
        public bool IsRecruting = true;

        public TacticalGroup(string name)
        {
            Units = new List<Entity>();
            Name = name;
        }
    }
}