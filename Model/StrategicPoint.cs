using System.Collections.Generic;
using Aicup2020.Model;

namespace Aicup2020
{
    public class StrategicPoint
    {
        public int Id;
        public List<TacticalGroup> Groups;
        public Vec2Int Position;
        public StrategicPoint NextPoint;
        public bool IsCaptured;

        public StrategicPoint(Vec2Int position, int id)
        {
            Position = position;
            Groups = new List<TacticalGroup>();
            Id = id;
        }
    }
}