using System;
using System.Collections.Generic;
using System.Linq;
using Aicup2020.Model;
using Action = Aicup2020.Model.Action;

namespace Aicup2020
{

    public class MyStrategy
    {
        // private bool _isAccumulateResources;
        const bool IS_DEBUG = true;
        private int _myIndex = -1;
        private int _mapSize = 1;

        EntityType[] housesEntityTypes =
            {EntityType.House, EntityType.BuilderBase, EntityType.MeleeBase, EntityType.RangedBase};

        EntityType[] unitEntityTypes = {EntityType.BuilderUnit, EntityType.MeleeUnit, EntityType.RangedUnit};

        const int GROUP_MIN = 20;

        const int MAX_TURRET_REMOTENESS = 6;

        Vec2Int bunchPosition1 = new Vec2Int(7, 20);
        Vec2Int bunchPosition2 = new Vec2Int(20, 7);
        // Vec2Int bunchPosition1 = new Vec2Int(15, 15);
        // Vec2Int bunchPosition2 = new Vec2Int(15, 15);

        const int MIN_BUILDERS = 15;
        Entity[] scouts = new Entity[3];
        private bool[] scoutStats = new bool[3];
        Vec2Int[] enemyRespPositions = new Vec2Int[3];

        private AutoAttack _builderAutoAttack;
        private AutoAttack _unitAutoAttack;
        private MoveAction _defaultBuilderMoveAction;
        private MoveAction _defaultBuilderRunAction;
        private IEnumerable<Entity> _myEntities;
        private IEnumerable<Entity> _allBuildings;
        private IEnumerable<Entity> _houses;
        private IEnumerable<Entity> _bases;
        private IEnumerable<Entity> _builders;
        private List<Entity> _availableBuilders;
        private IEnumerable<Entity> _soldiers;
        private IEnumerable<Entity> _turrets;
        private IDictionary<EntityType, EntityProperties> _props;
        private IEnumerable<Entity> _enemyEntities;
        private Player[] _players;

        private Entity[] _playerViewEntities;
        
        Dictionary<int, bool> _baseProductionStatuses = new Dictionary<int, bool>();
        const float HOUSE_PRODUCTION_MODIFIER = 0.02f; // HPM х population = houseCount to build in 1 tick 
        const float BUILDERS_MULTIPLIER = 1.2f; // BM * soldiers.Count = max_builders limit

        public Action GetAction(PlayerView playerView, DebugInterface debugInterface)
        {
            var myId = playerView.MyId;

            _players = playerView.Players;
            _playerViewEntities = playerView.Entities;
            _myEntities = _playerViewEntities.Where(entity => entity.PlayerId == myId).ToList();
            _allBuildings = _myEntities.Where(e => !Array.Exists(unitEntityTypes, t => t == e.EntityType));
            _houses = _myEntities.Where(e => e.EntityType == EntityType.House);
            _bases = _myEntities.Where(e => e.EntityType == EntityType.BuilderBase
                                            || e.EntityType == EntityType.MeleeBase
                                            || e.EntityType == EntityType.RangedBase);
            _builders = _myEntities.Where(entity => entity.EntityType == EntityType.BuilderUnit);
            _soldiers = _myEntities.Where(entity =>
                entity.EntityType == EntityType.MeleeUnit || entity.EntityType == EntityType.RangedUnit);
            _turrets = _myEntities.Where(e => e.EntityType == EntityType.Turret);
            _enemyEntities = _playerViewEntities.Where(entity => entity.PlayerId > 0 && entity.PlayerId != myId);
            _availableBuilders = new List<Entity>();

            InitConstsFromPlayerVew(playerView); // init consts once

            Action result = new Action(new System.Collections.Generic.Dictionary<int, Model.EntityAction>());

            // default builder commands
            SetBuildersOrders(result);

            // default turret attack actions
            SetTurretsAutoattack(result);

            // soldiers orders
            SetSoldiersOrders(result);

            // build units
            BuildUnits(result);

            return result;
        }

        private void InitConstsFromPlayerVew(PlayerView playerView)
        {
            if (_myIndex == -1)
            {
                _mapSize = playerView.MapSize;
                var rangeLimit = _mapSize - 5;
                enemyRespPositions[0] = new Vec2Int(5, rangeLimit);
                enemyRespPositions[1] = new Vec2Int(rangeLimit, rangeLimit);
                enemyRespPositions[2] = new Vec2Int(rangeLimit, 5);
                for (var i = 0; i < _players.Length; i++)
                {
                    if (_players[i].Id == playerView.MyId)
                    {
                        _myIndex = i;
                    }
                }

                _builderAutoAttack = new AutoAttack(playerView.EntityProperties[EntityType.BuilderUnit].SightRange,
                    new EntityType[] {EntityType.Resource});
                _unitAutoAttack = new AutoAttack(playerView.EntityProperties[EntityType.BuilderUnit].SightRange,
                    new EntityType[0]);
                _defaultBuilderMoveAction = new MoveAction(
                    new Vec2Int(playerView.MapSize - 1, playerView.MapSize - 1),
                    true,
                    true);
                _defaultBuilderRunAction = new MoveAction(
                    new Vec2Int(0, 0),
                    true,
                    true);
                _props = playerView.EntityProperties;
            }
        }

        private void BuildUnits(Action result)
        {

            foreach (var unitBase in _bases)
            {
                // add base to production statuses dict
                if (!_baseProductionStatuses.ContainsKey(unitBase.Id))
                {
                    _baseProductionStatuses.Add(unitBase.Id, false);
                }
                
                switch (unitBase.EntityType)
                {
                    case EntityType.BuilderBase:
                    {
                        var buildersCount = _builders.Count();
                        if (buildersCount > MIN_BUILDERS && buildersCount > _soldiers.Count() * BUILDERS_MULTIPLIER)
                        {
                            StopProduction(result, unitBase);
                        }
                        else
                        {
                            StartProduction(result, unitBase);
                        }

                        break;
                    }
                    case EntityType.MeleeBase:
                    {
                        StartProduction(result, unitBase);
                        break;
                    }
                    case EntityType.RangedBase:
                    {
                        StartProduction(result, unitBase);
                        break;
                    }
                }
            }
        }

        private void StopProduction(Action result, Entity unitBase)
        {
            if (_baseProductionStatuses[unitBase.Id] == true)
            {
                result.EntityActions[unitBase.Id] = new EntityAction(null, null, null, null);
                _baseProductionStatuses[unitBase.Id] = false;
            }
        }

        private void StartProduction(Action result, Entity unitBase)
        {
            // if (_baseProductionStatuses[unitBase.Id] == false)
            // {
            // var position = new Vec2Int(unitBase.Position.X + _props[unitBase.EntityType].Size,
            //     unitBase.Position.Y + _props[unitBase.EntityType].Size - 1);
            // var size = _props[unitBase.EntityType].Size;
            Vec2Int position = GetPositionToOut(unitBase);
            result.EntityActions[unitBase.Id] = new EntityAction(
                null,
                new BuildAction(
                    _props[unitBase.EntityType].Build.Value.Options[0],
                    position
                ),
                null,
                null
            );
            _baseProductionStatuses[unitBase.Id] = true;
            // }
        }


        private Vec2Int GetPositionToOut(Entity building)
        {
            var size = _props[building.EntityType].Size;
            var pos = new Vec2Int(building.Position.X + size, building.Position.Y + size);
            // top, right - left
            var count = 0;
            while (count < size)
            {
                pos.X -= 1;
                if (!_playerViewEntities.Any(s => s.Position.X == pos.X && s.Position.Y == pos.Y))

                    return pos;
                count += 1;
            }

            // right, up - down
            pos.X = building.Position.X + size;
            pos.Y = building.Position.Y + size;
            count = 0;
            while (count < size)
            {
                pos.Y -= 1;
                if (!_playerViewEntities.Any(s => s.Position.X == pos.X && s.Position.Y == pos.Y))

                    return pos;
                count += 1;
            }

            //left up-down
            pos.X = building.Position.X - 1;
            pos.Y = building.Position.Y + size;
            count = 0;
            while (count < size)
            {
                pos.Y -= 1;
                if (!_playerViewEntities.Any(s => s.Position.X == pos.X && s.Position.Y == pos.Y))

                    return pos;
                count += 1;
            }

            //bottom right-left
            pos.X = building.Position.X + size;
            pos.Y = building.Position.Y - 1;
            count = 0;
            while (count < size)
            {
                pos.X -= 1;
                if (!_playerViewEntities.Any(s => s.Position.X == pos.X && s.Position.Y == pos.Y))

                    return pos;
                count += 1;
            }

            return new Vec2Int(0, 0);
        }


        private void SetSoldiersOrders(Action result)
        {
            //default soldier orders to autoattack
            // target to enemy with hiest wisible entities
            var enemyGroups = _enemyEntities.GroupBy(
                e => e.PlayerId,
                e => e,
                (pId, entities) => new
                {
                    Key = pId,
                    Count = entities.Count(),
                });
            var maxGroupCount = -1;
            var targetPlayerId = -1;
            foreach (var enemyGroup in enemyGroups)
            {
                if (enemyGroup.Count > maxGroupCount)
                {
                    maxGroupCount = enemyGroup.Count;
                    targetPlayerId = enemyGroup.Key.Value;
                }
            }

            var firstEnemy = _enemyEntities.FirstOrDefault(e => e.PlayerId == targetPlayerId);

            Vec2Int targetPosition;
            var bunchCounter1 = 0;
            var bunchCounter2 = 0;
            var isBunching = true;
            if (_soldiers.Count() < GROUP_MIN || firstEnemy.Id < 1)
            {
                targetPosition = bunchPosition1;
                isBunching = true;
            }
            else
            {
                targetPosition = firstEnemy.Position;
                isBunching = false;
            }

            scoutStats = new bool[] {false, false, false};
            var scoutIds = scouts.Select(s => s.Id).ToArray();

            foreach (var soldier in _soldiers)
            {
                var indexOf = Array.IndexOf(scoutIds, soldier.Id);
                if (indexOf > -1)
                {
                    scoutStats[indexOf] = true; // steel alive
                    result.EntityActions[soldier.Id] = new EntityAction(
                        new MoveAction(
                            enemyRespPositions[indexOf],
                            true,
                            true),
                        null,
                        new AttackAction(
                            null,
                            _unitAutoAttack
                        ),
                        null
                    );
                }
                else
                {
                    if (isBunching)
                    {
                        if (bunchCounter1 > bunchCounter2)
                        {
                            targetPosition = bunchPosition2;
                            bunchCounter2 += 1;
                        }
                        else
                        {
                            targetPosition = bunchPosition1;
                            bunchCounter1 += 1;
                        }
                    }

                    result.EntityActions[soldier.Id] = new EntityAction(
                        new MoveAction(
                            targetPosition,
                            true,
                            true),
                        null,
                        new AttackAction(
                            null,
                            _unitAutoAttack
                        ),
                        null
                    );
                }
            }

            for (var i = 0; i < scoutStats.Length; i++)
            {
                if (scoutStats[i] == false)
                {
                    // new scout
                    scouts[i] = new Entity();
                    var candidat = _soldiers.FirstOrDefault(soldier => Array.IndexOf(scoutIds, soldier.Id) < 0);
                    if (candidat.Id > 0)
                    {
                        scoutStats[i] = true;
                        scouts[i] = candidat;
                        result.EntityActions[candidat.Id] = new EntityAction(
                            new MoveAction(
                                enemyRespPositions[i],
                                true,
                                true),
                            null,
                            new AttackAction(
                                null,
                                _unitAutoAttack
                            ),
                            null
                        );
                    }
                }
            }

            // range units run from meelee
            var archers = _soldiers.Where(e => e.EntityType == EntityType.RangedUnit);
            foreach (var archer in archers)
            {
                var dangerNearEnemy =
                    _enemyEntities.FirstOrDefault(e => e.EntityType == EntityType.MeleeUnit && GetRange(e, archer) < 3);

                if (dangerNearEnemy.Id > 0)
                {
                    result.EntityActions[archer.Id] = new EntityAction(
                        _defaultBuilderRunAction,
                        null,
                        null,
                        null
                    );
                }
            }
        }

        private void SetTurretsAutoattack(Action result)
        {
            foreach (var turret in _turrets)
            {
                result.EntityActions[turret.Id] = new EntityAction(
                    null,
                    null,
                    new AttackAction(
                        null,
                        _unitAutoAttack
                    ),
                    null
                );
            }
        }

        private void SetBuildersOrders(Action result)
        {
            var popAvailable = _houses.Count() * _props[EntityType.House].PopulationProvide +
                               _bases.Count() * _props[EntityType.MeleeBase].PopulationProvide;
            int unitsCount = _soldiers.Count() + _builders.Count();
            int plan = 0;
            int buildHouseCount = 0;
            if (unitsCount >= popAvailable &&
                _players[_myIndex].Resource >= _props[EntityType.House].InitialCost)
            {
                buildHouseCount = 1 + Convert.ToInt32(Math.Floor(unitsCount * HOUSE_PRODUCTION_MODIFIER));
                plan += _props[EntityType.House].InitialCost * buildHouseCount;
            }

            var isBuildBuilderBase = _bases.FirstOrDefault(b => b.EntityType == EntityType.BuilderBase).Id == 0
                                     && _players[_myIndex].Resource >=
                                     _props[EntityType.BuilderBase].InitialCost + plan;
            if (isBuildBuilderBase) plan += _props[EntityType.BuilderBase].InitialCost;
            var isBuildMeleeBase = _bases.FirstOrDefault(b => b.EntityType == EntityType.MeleeBase).Id == 0
                                   && _players[_myIndex].Resource >= _props[EntityType.MeleeBase].InitialCost + plan;
            if (isBuildMeleeBase) plan += _props[EntityType.MeleeBase].InitialCost;
            var isBuildRangedBase = _bases.FirstOrDefault(b => b.EntityType == EntityType.RangedBase).Id == 0
                                    && _players[_myIndex].Resource >= _props[EntityType.RangedBase].InitialCost + plan;
            if (isBuildRangedBase) plan += _props[EntityType.RangedBase].InitialCost;
            bool isTurretBuildOrdered = false;

            // build orders
            foreach (var builder in _builders)
            {
                var battleEnemies = _enemyEntities.Where(e =>
                    e.EntityType != EntityType.BuilderUnit && _props[e.EntityType].Attack.HasValue);
                var nearEnemies = battleEnemies.Where(e => IsEntityInRange(e, 15, builder.Position));
                
                // run
                if (nearEnemies.Any(e =>
                    (e.EntityType == EntityType.MeleeUnit && GetRange(e, builder) < 4) // danger zone to melee=3 1-range, 1-builderMove, 1-meleeMove
                    || (e.EntityType == EntityType.RangedUnit && GetRange(e, builder) < 8) // danger zone to ranged =7 5-range 1-builderMove, 1-rangedMove
                    || (e.EntityType == EntityType.Turret && GetRange(e, builder) < 7))) // danger zone to turret =6 5-range 1-builderMove
                {
                    var runPosition = GetPositionRunTo(builder, builder); //todo fix


                    //var runPosition = new Vec2Int(0, 0);
                    result.EntityActions[builder.Id] = new EntityAction(
                        new MoveAction(runPosition, true, true),
                        null,
                        null,
                        null
                    );
                    continue;
                }

                if (!nearEnemies.Any())
                {
                    // build house
                    if (buildHouseCount > 0)
                    {
                        if (TrySetBuildOrder(result, builder, EntityType.House))
                        {
                            buildHouseCount -= 1;
                            continue;
                        }
                    }

                    // build builder base
                    if (isBuildBuilderBase)
                    {
                        if (TrySetBuildOrder(result, builder, EntityType.BuilderBase))
                        {
                            isBuildBuilderBase = false;
                            continue;
                        }
                    }

                    // build melee base
                    if (isBuildMeleeBase)
                    {
                        if (TrySetBuildOrder(result, builder, EntityType.MeleeBase))
                        {
                            isBuildMeleeBase = false;
                            continue;
                        }
                    }

                    // build Ranged base
                    if (isBuildRangedBase)
                    {
                        if (TrySetBuildOrder(result, builder, EntityType.RangedBase))
                        {
                            isBuildRangedBase = false;
                            continue;
                        }
                    }

                    //build turret
                    if (!isTurretBuildOrdered
                        && builder.Position.X > 15 // do not build turrets on back of base
                        && builder.Position.Y > 15
                        && _players[_myIndex].Resource >= plan + _props[EntityType.Turret].InitialCost
                        && !_turrets.Any(t => IsEntityInRange(t, MAX_TURRET_REMOTENESS, builder.Position)))
                    {
                        if (TrySetBuildOrder(result, builder, EntityType.Turret))
                        {
                            plan += _props[EntityType.Turret].InitialCost;
                            isTurretBuildOrdered = true;
                            continue;
                        }
                    }
                }
                
                // builders without building orders
                _availableBuilders.Add(builder);
            }

            // repair soldiers
            for (var i = _availableBuilders.Count - 1; i >= 0; i--)
            {
                var b = _availableBuilders[i];
                var repairedSoldier = _soldiers.FirstOrDefault(s =>
                    s.Health < _props[s.EntityType].MaxHealth
                    && ((s.Position.X == b.Position.X - 1 && s.Position.Y == b.Position.Y)
                        || (s.Position.X == b.Position.X + 1 && s.Position.Y == b.Position.Y)
                        || (s.Position.X == b.Position.X && s.Position.Y == b.Position.Y - 1)
                        || (s.Position.X == b.Position.X && s.Position.Y == b.Position.Y + 1))
                );
                if (repairedSoldier.Id > 0)
                {
                    result.EntityActions[b.Id] = new EntityAction(
                        null,
                        null,
                        null,
                        new RepairAction(repairedSoldier.Id));
                    _availableBuilders.RemoveAt(i);
                }
            }

            //repair buildings
            foreach (var building in _allBuildings)
            {
                if (building.Health < _props[building.EntityType].MaxHealth)
                {
                    var size = _props[building.EntityType].Size;
                    var offset = size / 2;
                    for (var i = 0; i < size; i++)
                    {
                        // command nearest bilder to repair
                        var builder = GetNearestEntity(_availableBuilders, building);
                        if (builder.HasValue)
                        {
                            result.EntityActions[builder.Value.Id] = new EntityAction(
                                new MoveAction(new Vec2Int(building.Position.X + offset, building.Position.Y + offset),
                                    true, true),
                                null,
                                null,
                                new RepairAction(building.Id));
                            _availableBuilders.Remove(builder.Value);
                        }
                    }
                }
            }

            // harvest
            IEnumerable<Entity> allRes = new List<Entity>();
            var allResCount = 0;
            var range = 10;
            while (allResCount < 10 && range < _mapSize)
            {
                allRes = _playerViewEntities.Where(res =>
                    res.EntityType == EntityType.Resource && res.Position.X + res.Position.Y < range);
                allResCount = allRes.Count();
                range += 10;
            }

            // any resource with available place for builder
            var availableRes = allRes.Where(r =>
                (r.Position.X > 0 && !allRes.Any(edgeRes => edgeRes.Position.X == r.Position.X - 1 && edgeRes.Position.Y == r.Position.Y && r.Position.X > 0))
                || (r.Position.X < _mapSize-1 && !allRes.Any(edgeRes => edgeRes.Position.X == r.Position.X + 1 && edgeRes.Position.Y == r.Position.Y && r.Position.X < _mapSize - 1))
                || (r.Position.Y > 0 && !allRes.Any(edgeRes => edgeRes.Position.X == r.Position.X && edgeRes.Position.Y == r.Position.Y - 1 && r.Position.Y > 0))
                || (r.Position.Y < _mapSize-1 && !allRes.Any(edgeRes => edgeRes.Position.X == r.Position.X && edgeRes.Position.Y == r.Position.Y + 1 && r.Position.Y < _mapSize - 1))
            ).ToList();

            var enemySoldiers = _enemyEntities.Where(enemy =>
                enemy.EntityType == EntityType.Turret || enemy.EntityType == EntityType.MeleeUnit ||
                enemy.EntityType == EntityType.RangedUnit).ToList();

            for (var i = _availableBuilders.Count - 1; i >= 0; i--)
            {
                var b = _availableBuilders[i];

                var resource = GetNearestEntity(availableRes.Where(res =>
                    _builders.Count(b => IsEntityInRange(b, 1, res.Position)) < 2
                    && !enemySoldiers.Any(enemy => IsEntityInRange(enemy, 10, res.Position))
                ), b);

                if (resource.HasValue)
                {
                    result.EntityActions[b.Id] = new EntityAction(
                        new MoveAction(resource.Value.Position, true, true),
                        null,
                        new AttackAction(
                            null,
                            _builderAutoAttack
                        ),
                        null);
                    availableRes.Remove(resource.Value);
                }
                else
                {
                    result.EntityActions[b.Id] = new EntityAction(
                        new MoveAction(new Vec2Int(_mapSize / 2, _mapSize / 2), true, true),
                        null,
                        new AttackAction(
                            null,
                            _builderAutoAttack
                        ),
                        null);
                }

                _availableBuilders.RemoveAt(i);
            }
        }

        private bool TrySetBuildOrder(Action result, Entity builder, EntityType entityType)
        {
            var newBuildingPosition = GetNewBuildingPosition(builder, entityType);
            if (IsGoodPosition(newBuildingPosition))
            {
                result.EntityActions[builder.Id] = new EntityAction(
                    null, new BuildAction(
                        entityType,
                        newBuildingPosition
                    ), null, null);
                return true;
            }

            return false;
        }

        private Vec2Int GetPositionRunTo(Entity runner, Entity dangerNearEnemy)
        {
            // var runX = runner.Position.X + (runner.Position.X - dangerNearEnemy.Position.X);
            // var runY = runner.Position.Y + (runner.Position.Y - dangerNearEnemy.Position.Y);
            // var runPosition = new Vec2Int(runX, runY);
            // FixPositionOnMapEdge(ref runPosition);
            // return runPosition;
            return new Vec2Int(0, 0);

            // return _bases.FirstOrDefault().Position;
        }

        private bool IsGoodPosition(Vec2Int newBuildingPosition)
        {
            if (newBuildingPosition.X < 0 || newBuildingPosition.Y < 0) return false;
            if (newBuildingPosition.X > _mapSize - 5 || newBuildingPosition.Y > _mapSize - 5) return false;
            return true;
        }

        private void FixPositionOnMapEdge(ref Vec2Int position)
        {
            if (position.X < 0) position.X = 0;
            if (position.X >= _mapSize) position.X = _mapSize - 1;
            if (position.Y < 0) position.Y = 0;
            if (position.Y >= _mapSize) position.Y = _mapSize - 1;
            // todo move to nearest units/turrets 

            // if (position.X < 0)
            // {
            //     position.X = 0;
            //     if (position.Y < _mapSize / 2)
            //     {
            //         position.Y = 0;
            //     }
            //     else
            //     {
            //         position.Y = _mapSize - 1;
            //     }
            // }
        }

        private Player? GetPoweredEnemy(PlayerView playerView)
        {
            var maxScore = -1;

            // var maxEntitiesCount = -1;
            Player? poweredPlayer = null;
            for (var i = 0;
                i < playerView.Players.Length;
                i++)
            {
                if (i == _myIndex)
                {
                    continue;
                }

                var player = playerView.Players[i];
                var entitiesCount = playerView.Entities.Count(e => e.PlayerId == player.Id);
                if (entitiesCount < 1)
                {
                    continue;
                }

                if (player.Score > maxScore)
                {
                    poweredPlayer = player;
                    maxScore = poweredPlayer.Value.Score;
                }
            }

            return poweredPlayer;
        }

        private Vec2Int GetNewBuildingPosition(Entity builder, EntityType buildingType)
        {
            // 1 bottom, left -> right
            var size = _props[buildingType].Size;

            // var offset = new Vec2Int(-1 * size, -1 * size);
            var pos = new Vec2Int(builder.Position.X - size, builder.Position.Y - size);

            var count = 0;
            while (count < size)
            {
                pos.X += 1;
                if (IsPositionClearToBuild(pos, size))
                    return pos;
                count += 1;
            }

            // 2 left, down -> up
            pos.X = builder.Position.X - size;
            pos.Y = builder.Position.Y - size;
            count = 0;
            while (count < size)
            {
                pos.Y += 1;
                if (IsPositionClearToBuild(pos, size))
                    return pos;
                count += 1;
            }

            //3 top left -> right
            pos.X = builder.Position.X - size;
            pos.Y = builder.Position.Y + 1;
            count = 0;
            while (count < size)
            {
                pos.X += 1;
                if (IsPositionClearToBuild(pos, size))
                    return pos;
                count += 1;
            }

            //3 right down -> up
            pos.X = builder.Position.X + 1;
            pos.Y = builder.Position.Y - size;
            count = 0;
            while (count < size)
            {
                pos.Y += 1;
                if (IsPositionClearToBuild(pos, size))
                    return pos;
                count += 1;
            }

            return new Vec2Int(-1, -1);
        }

        private bool IsPositionClearToBuild(Vec2Int pos, int size)
        {
            var entityInPlace = _playerViewEntities.FirstOrDefault(e =>
                e.Position.X >= pos.X
                && e.Position.Y >= pos.Y
                && e.Position.X < pos.X + size
                && e.Position.Y < pos.Y + size);
            if (entityInPlace.Id > 0) return false;

            var buildingsOnLine1 = _allBuildings.FirstOrDefault(e =>
                e.Position.X >= pos.X - 1
                && e.Position.Y >= pos.Y - 1
                && e.Position.X <= pos.X + size
                && e.Position.Y <= pos.Y + size);
            if (buildingsOnLine1.Id > 0) return false;

            var buildingOnLeftBottomRange2ExceptWall = _allBuildings.FirstOrDefault(e =>
                e.EntityType != EntityType.Wall
                && e.Position.X >= pos.X - 2
                && e.Position.Y >= pos.Y - 2
                && e.Position.X <= pos.X + size
                && e.Position.Y <= pos.Y + size);
            if (buildingOnLeftBottomRange2ExceptWall.Id > 0) return false;

            var houseOrBaseOnLeftBottomRange3 = _allBuildings.FirstOrDefault(e =>
                e.EntityType != EntityType.Wall
                && e.EntityType != EntityType.Turret
                && e.Position.X >= pos.X - 3
                && e.Position.Y >= pos.Y - 3
                && e.Position.X <= pos.X + size
                && e.Position.Y <= pos.Y + size);
            if (houseOrBaseOnLeftBottomRange3.Id > 0) return false;

            var anyBaseOnLeftBottomRange4 = _bases.FirstOrDefault(e =>
                e.Position.X >= pos.X - 5
                && e.Position.Y >= pos.Y - 5
                && e.Position.X <= pos.X + size
                && e.Position.Y <= pos.Y + size);
            if (anyBaseOnLeftBottomRange4.Id > 0) return false;
            return true;
        }

        private static Entity? GetNearestEntity(IEnumerable<Entity> entities, Entity targetEntity)
        {
            Entity? nearestBuilder = null;

            int range = 100000;
            foreach (var entity in entities)
            {
                var b_range = GetRange(targetEntity, entity);
                if (b_range < range)
                {
                    nearestBuilder = entity;
                    range = b_range;
                }
            }

            return nearestBuilder;
        }

        private static int GetRange(Entity targetEntity, Entity builder)
        {
            return Math.Abs(builder.Position.X - targetEntity.Position.X) +
                   Math.Abs(builder.Position.Y - targetEntity.Position.Y);
        }

        private static bool IsEntityInRange(Entity entity, int range, Vec2Int basePos)
        {
            if (entity.Position.X > basePos.X + range
                || entity.Position.Y > basePos.Y + range
                || entity.Position.X < basePos.X - range
                || entity.Position.Y < basePos.Y - range) return false;
            return true;
        }

        public void DebugUpdate(PlayerView playerView, DebugInterface debugInterface)
        {
            debugInterface.Send(new DebugCommand.Clear());

            // var enemies = playerView.Entities.Where(e => e.PlayerId.HasValue && e.PlayerId.Value != playerView.MyId);
            // foreach (var enmy in enemies)
            // {
            //     var v = new ColoredVertex(new Vec2Float(enmy.Position.X, enmy.Position.Y), offset, color);
            //     debugInterface.Send(new DebugCommand.Add(new DebugData.PlacedText(v,
            //         $"{enmy.Id} [{enmy.Position.X},{enmy.Position.Y}]", 0f, 12f)));
            // }
            if (IS_DEBUG)
            {
                var offset = new Vec2Float(0f, 0.7f);
                var color = new Color(1f, 0f, 0f, 1f);

                // 10x10 grid
                var vertexes = new List<ColoredVertex>();
                for (var i = 0; i < playerView.MapSize / 10; i++)
                {
                    // vertical line
                    var tenPow = i * 10;
                    vertexes.Add(new ColoredVertex(new Vec2Float(tenPow, 0), offset, color));
                    vertexes.Add(new ColoredVertex(new Vec2Float(tenPow, playerView.MapSize), offset, color));

                    // horisontal line
                    vertexes.Add(new ColoredVertex(new Vec2Float(0, tenPow), offset, color));
                    vertexes.Add(new ColoredVertex(new Vec2Float(playerView.MapSize, tenPow), offset, color));

                    debugInterface.Send(
                        new DebugCommand.Add(new DebugData.Primitives(vertexes.ToArray(), PrimitiveType.Lines)));

                    debugInterface.Send(new DebugCommand.Add(
                        new DebugData.PlacedText(
                            new ColoredVertex(new Vec2Float(0, tenPow), offset, color),
                            $"{tenPow}", 0f, 12f)));

                    debugInterface.Send(new DebugCommand.Add(
                        new DebugData.PlacedText(
                            new ColoredVertex(new Vec2Float(tenPow, 0), offset, color),
                            $"{tenPow}", 0f, 12f)));
                }

                // show factory statuses
                var bases = playerView.Entities.Where(e => e.EntityType == EntityType.BuilderBase
                                                           || e.EntityType == EntityType.MeleeBase
                                                           || e.EntityType == EntityType.RangedBase);
                foreach (var factory in bases)
                {
                    if (!_baseProductionStatuses.ContainsKey(factory.Id)) continue;
                    var status = _baseProductionStatuses[factory.Id] ? "ACTIVE" : "----";
                    debugInterface.Send(new DebugCommand.Add(
                        new DebugData.PlacedText(
                            new ColoredVertex(new Vec2Float(factory.Position.X + 2, factory.Position.Y + 2), offset,
                                color),
                            $"{status}", 0f, 14f)));
                }

                //display resources
                debugInterface.Send(new DebugCommand.Add(
                    new DebugData.Log("res:" + playerView.Entities.Count(e => e.EntityType == EntityType.Resource))));
            }

            // var v1 = new ColoredVertex(new Vec2Float(1f,1f),offset,color );
            // var v2 = new ColoredVertex(new Vec2Float(1f,5f),offset,color);
            // var v3 = new ColoredVertex(new Vec2Float(5f,0f),offset,color );
            // var v4 = new ColoredVertex(new Vec2Float(5f,20f),offset,color);
            // var coloredVertices = new ColoredVertex[4] {v1, v2, v3, v4};
            // debugInterface.Send(new DebugCommand.Add(new DebugData.Primitives(coloredVertices,PrimitiveType.Lines )));
            var state = debugInterface.GetState();
        }
    }
}