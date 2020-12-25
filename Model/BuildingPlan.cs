namespace Aicup2020.Model
{
    public class BuildingPlan
    {
        private Vec2Int position;
        private EntityType entityType;

        public Vec2Int Position
        {
            get => position;
            set => position = value;
        }

        public EntityType EntityType
        {
            get => entityType;
            set => entityType = value;
        }

        public BuildingPlan(Vec2Int position, EntityType entityType)
        {
            this.position = position;
            this.entityType = entityType;
        }
    }
}