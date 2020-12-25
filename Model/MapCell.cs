namespace Aicup2020.Model
{
    public struct MapCell
    {
        public EntityType entityType;
        public bool isEmpty;

        public MapCell(EntityType entityType, bool isEmpty)
        {
            this.entityType = entityType;
            this.isEmpty = isEmpty;
        }
    }
}