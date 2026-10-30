// Míssil Intercontinental - todo o código do mod em um único arquivo.
// Cada bloco "namespace" abaixo era um arquivo separado (o nome original está no comentário).

// ===== IcbmDefOf.cs =====
namespace IntercontinentalMissile
{
    using RimWorld;
    using RimWorld.Planet;
    using Verse;
    using Verse.AI;

    [DefOf]
    public static class IcbmDefOf
    {
        public static WorldObjectDef Icbm_InFlight;
        public static JobDef Icbm_LoadMissile;
        public static JobDef Icbm_UnloadMissile;

        static IcbmDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(IcbmDefOf));
        }
    }
}

// ===== MissileStats.cs =====
namespace IntercontinentalMissile
{
    using Verse;

    /// <summary>
    /// Características de cada tipo de míssil. Fica no ThingDef do míssil (modExtensions),
    /// então para criar um míssil novo basta definir outro item com estes valores.
    /// </summary>
    public class MissileStats : DefModExtension
    {
        public float blastRadius = 22f;        // raio da explosão (células)
        public int blastDamage = 350;          // dano no centro (cai com a distância)
        public float coreRadiusFactor = 0.35f; // raio do núcleo = raio * este fator
        public float coreDamageFactor = 2f;    // dano do núcleo = dano * este fator
        public float fireChance = 0.1f;        // chance de fogo na onda principal
        public float coreFireChance = 0.3f;    // chance de fogo no núcleo
        public float buildingDamageMultiplier = 2f; // quanto a onda de choque pesa contra construções (maior = destrói mais)

        public string loadedSiloTexture;       // imagem do silo quando este míssil está carregado (vazio = padrão)
        public ThingDef fallingMote;           // visual do míssil caindo
        public float fallingScale = 5f;        // tamanho do míssil caindo (em células de altura)
        public ThingDef launchMote;            // visual do míssil decolando do silo
        public float launchScale = 5f;         // tamanho do míssil decolando
        public float exhaustScale = 1f;        // tamanho da chama e da fumaça na decolagem (1 = padrão)
    }
}

// ===== CompIcbm.cs =====
namespace IntercontinentalMissile
{
    using System.Collections.Generic;
    using RimWorld;
    using RimWorld.Planet;
    using UnityEngine;
    using Verse;

    public class CompProperties_Icbm : CompProperties
    {
        public int minRange = 1;        // distância mínima em tiles (1 = sem limite prático)
        public int maxRange = 500;      // distância máxima em tiles
        public int ticksPerTile = 150;  // tempo de voo por tile
        public float fuelCost = 1f;     // mísseis consumidos por lançamento
        public int cooldownTicks = 300000; // recarga entre lançamentos (5 dias)
        public float blastRadius = 22f;    // raio da explosão (em células do mapa do alvo)
        public int blastDamage = 350;      // dano no centro (cai com a distância; o núcleo recebe o dobro)
        public int mapHoldTicks = 900000;  // por quanto tempo o mapa bombardeado é preservado (15 dias)

        public CompProperties_Icbm()
        {
            compClass = typeof(CompIcbm);
        }
    }

    public class CompIcbm : ThingComp
    {
        private CompProperties_Icbm Props => (CompProperties_Icbm)props;
        private CompRefuelable Fuel => parent.GetComp<CompRefuelable>();
        private CompPowerTrader Power => parent.GetComp<CompPowerTrader>();

        private int nextLaunchTick;

        // Tipo de míssil carregado (preenchido pelo patch do Harmony ao abastecer o silo)
        public ThingDef loadedMissile;

        private int CooldownLeft => nextLaunchTick - Find.TickManager.TicksGame;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref nextLaunchTick, "nextLaunchTick");
            Scribe_Defs.Look(ref loadedMissile, "loadedMissile");
        }

        // Animação de decolagem (não é salva)
        private const int LaunchTicks = 900;      // duração máxima (15 s); termina antes, quando o míssil sai da tela
        private const int HatchTicks = 45;        // tempo em que o silo continua mostrando o míssil dentro
        private const float StartScaleFactor = 0.6f; // o míssil começa menor (dentro do silo) e cresce
        private const int GrowTicks = 70;
        private MoteThrown launchMote;
        private int launchTicksLeft;
        private float launchScale = 5f;
        private float exhaustScale = 1f;
        private IntVec3 launchStartCell;
        private float launchStartZ;

        public ThingDef launchingMissile;         // míssil em decolagem (para o silo manter a imagem aberta)

        public bool HoldLoadedLook
        {
            get { return launchTicksLeft > LaunchTicks - HatchTicks && launchingMissile != null; }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (parent.IsHashIntervalTick(15))
            {
                (parent as Building_IcbmSilo)?.RefreshGraphicIfNeeded();
            }
            if (launchTicksLeft > 0)
            {
                TickLaunchAnimation();
            }
        }

        private void StartLaunchAnimation(MissileStats stats, ThingDef usedMissileForAnim)
        {
            Map map = parent.Map;
            if (map == null || !parent.Spawned)
            {
                return;
            }

            ThingDef moteDef = stats != null ? stats.launchMote : null;
            if (moteDef == null)
            {
                moteDef = DefDatabase<ThingDef>.GetNamedSilentFail("Icbm_LaunchMote");
            }
            if (moteDef == null)
            {
                return;
            }

            MoteThrown mote = ThingMaker.MakeThing(moteDef) as MoteThrown;
            if (mote == null)
            {
                return;
            }

            launchScale = stats != null ? stats.launchScale : 5f;
            exhaustScale = stats != null ? stats.exhaustScale : 1f;
            launchStartCell = parent.Position;
            launchStartZ = parent.Position.ToVector3Shifted().z;
            mote.exactPosition = parent.Position.ToVector3Shifted();   // começa no centro do silo
            mote.Scale = launchScale * StartScaleFactor;
            mote.SetVelocity(0f, 0.3f);   // 0° = para cima na tela
            GenSpawn.Spawn(mote, parent.Position, map);
            launchMote = mote;
            launchTicksLeft = LaunchTicks;
            launchingMissile = usedMissileForAnim;
            (parent as Building_IcbmSilo)?.RefreshGraphicIfNeeded();
        }

        // O míssil sobe acelerando, deixando chama e fumaça
        private void TickLaunchAnimation()
        {
            launchTicksLeft--;
            Map map = parent.Map;
            // Só some quando já saiu por inteiro da área visível da câmera (ou se o jogador saiu deste mapa)
            bool outOfView = false;
            if (launchMote != null && map != null)
            {
                if (Find.CurrentMap != map)
                {
                    outOfView = true;
                }
                else
                {
                    float topOfView = Find.CameraDriver.CurrentViewRect.maxZ + 2f;
                    outOfView = (launchMote.exactPosition.z - launchScale * 0.5f) > topOfView;
                }
            }
            if (launchMote == null || launchMote.Destroyed || map == null || launchTicksLeft <= 0 || outOfView)
            {
                if (launchMote != null && !launchMote.Destroyed)
                {
                    launchMote.Destroy();
                }
                launchMote = null;
                launchTicksLeft = 0;
                launchingMissile = null;
                (parent as Building_IcbmSilo)?.RefreshGraphicIfNeeded();
                return;
            }

            int elapsed = LaunchTicks - launchTicksLeft;
            float t = elapsed / 60f;        // segundos desde a decolagem
            launchMote.SetVelocity(0f, 0.3f + (t / 3f) * (t / 3f) * 28f);

            // Cresce de leve enquanto sobe (efeito de se aproximar da câmera)
            float grow = Mathf.Clamp01(elapsed / (float)GrowTicks);
            float currentScale = launchScale * Mathf.Lerp(StartScaleFactor, 1f, grow);
            launchMote.Scale = currentScale;   // Mote.Scale só tem set, por isso guardamos o valor

            // Cauda: chama e fumaça logo abaixo do míssil
            Vector3 tail = launchMote.exactPosition - new Vector3(0f, 0f, currentScale * 0.5f);
            if (tail.ToIntVec3().InBounds(map))
            {
                float k = exhaustScale;
                FleckMaker.ThrowFireGlow(tail, map, 1.5f * k);
                if (launchTicksLeft % 2 == 0)
                {
                    FleckMaker.ThrowSmoke(tail, map, 2.5f * k);
                }
                if (launchTicksLeft % 6 == 0)
                {
                    FleckMaker.ThrowMicroSparks(tail, map);
                }

                // Míssil maior: mais fumaça e chama, espalhadas dos lados da cauda
                if (k > 1.2f)
                {
                    float spread = 0.6f * k;
                    for (int i = 0; i < 2; i++)
                    {
                        Vector3 side = tail + new Vector3(Rand.Range(-spread, spread), 0f, Rand.Range(-spread * 0.5f, 0f));
                        if (side.ToIntVec3().InBounds(map))
                        {
                            FleckMaker.ThrowSmoke(side, map, 2.5f * k);
                            FleckMaker.ThrowFireGlow(side, map, 1.2f * k);
                        }
                    }
                }
            }
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            if (launchMote != null && !launchMote.Destroyed)
            {
                launchMote.Destroy();
            }
            launchMote = null;
            launchTicksLeft = 0;
        }

        public override string CompInspectStringExtra()
        {
            string text = null;
            if (loadedMissile != null && Fuel != null && Fuel.Fuel >= 1f)
            {
                text = "Icbm_LoadedMissile".Translate(loadedMissile.LabelCap).ToString();
            }

            int left = CooldownLeft;
            if (left > 0)
            {
                string cooldown = "Icbm_SiloCooldown".Translate(left.ToStringTicksToPeriod()).ToString();
                text = text == null ? cooldown : text + "\n" + cooldown;
            }
            return text;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }

            if (parent.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            Command_Action cmd = new Command_Action
            {
                defaultLabel = "Icbm_LaunchLabel".Translate().ToString(),
                defaultDesc = "Icbm_LaunchDesc".Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/LaunchShip", true),
                action = StartTargeting
            };

            string reason = CannotLaunchReason();
            if (reason != null)
            {
                cmd.Disable(reason);
            }
            yield return cmd;
        }

        public bool HasMissile
        {
            get { return Fuel != null && Fuel.Fuel >= 1f; }
        }

        /// <summary>Tira o míssil do silo e o coloca no chão ao redor (usado pelo colono). Retorna false se não deu.</summary>
        public bool EjectLoadedMissile()
        {
            if (Fuel == null || Fuel.Fuel < 1f || !parent.Spawned)
            {
                return false;
            }

            ThingDef def = loadedMissile ?? DefDatabase<ThingDef>.GetNamedSilentFail("Icbm_Missile");
            if (def == null)
            {
                return false;
            }

            Thing missile = ThingMaker.MakeThing(def);
            if (!GenPlace.TryPlaceThing(missile, parent.Position, parent.Map, ThingPlaceMode.Near))
            {
                Messages.Message("Icbm_NoRoomAroundSilo".Translate().ToString(), parent, MessageTypeDefOf.RejectInput, false);
                missile.Destroy();
                return false;
            }

            Fuel.ConsumeFuel(Fuel.Fuel);
            loadedMissile = null;
            (parent as Building_IcbmSilo)?.RefreshGraphicIfNeeded();
            return true;
        }

        private string CannotLaunchReason()
        {
            if (Power != null && !Power.PowerOn)
            {
                return "Icbm_NoPower".Translate().ToString();
            }
            if (Fuel != null && Fuel.Fuel < Props.fuelCost)
            {
                return "Icbm_NoMissileLoaded".Translate().ToString();
            }
            if (CooldownLeft > 0)
            {
                return "Icbm_Recharging".Translate(CooldownLeft.ToStringTicksToPeriod()).ToString();
            }
            return null;
        }

        private void StartTargeting()
        {
            int origin = parent.Map.Tile;
            CameraJumper.TryJump(CameraJumper.GetWorldTarget(parent));
            Find.WorldTargeter.BeginTargeting(
                t => TryLaunchAt(t, origin),
                true,
                TexCommand.Attack,
                true);
        }

        private bool TryLaunchAt(GlobalTargetInfo target, int origin)
        {
            if (!target.IsValid)
            {
                Messages.Message("Icbm_InvalidTarget".Translate().ToString(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            int tile = target.Tile;
            if (tile < 0 || tile == origin)
            {
                Messages.Message("Icbm_InvalidTarget".Translate().ToString(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            int dist = Find.WorldGrid.TraversalDistanceBetween(origin, tile, true, Props.maxRange + 1);
            if (dist == int.MaxValue || dist > Props.maxRange)
            {
                Messages.Message("Icbm_OutOfRange".Translate(Props.maxRange).ToString(), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (dist < Props.minRange)
            {
                Messages.Message("Icbm_TooClose".Translate(Props.minRange).ToString(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            Settlement targetSettlement = null;
            foreach (WorldObject wo in Find.WorldObjects.AllWorldObjects)
            {
                if (wo.Tile == tile && wo is Settlement s)
                {
                    targetSettlement = s;
                    break;
                }
            }
            if (targetSettlement == null || targetSettlement.Faction == null)
            {
                Messages.Message("Icbm_NeedsOtherFaction".Translate().ToString(), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (targetSettlement.Faction == Faction.OfPlayer)
            {
                Messages.Message("Icbm_CannotAttackOwn".Translate().ToString(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            string reason = CannotLaunchReason();
            if (reason != null)
            {
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            // Tipo do míssil que está no silo (se não der para saber, usa o padrão)
            ThingDef usedMissile = loadedMissile ?? DefDatabase<ThingDef>.GetNamedSilentFail("Icbm_Missile");
            MissileStats stats = usedMissile != null ? usedMissile.GetModExtension<MissileStats>() : null;
            loadedMissile = null;

            Fuel?.ConsumeFuel(Props.fuelCost);
            nextLaunchTick = Find.TickManager.TicksGame + Props.cooldownTicks;
            (parent as Building_IcbmSilo)?.RefreshGraphicIfNeeded();

            MissileInFlight missile = (MissileInFlight)WorldObjectMaker.MakeWorldObject(IcbmDefOf.Icbm_InFlight);
            missile.Tile = origin;
            missile.origin = origin;
            missile.destination = tile;
            missile.ticksTotal = dist * Props.ticksPerTile;
            missile.ticksPassed = 0;
            missile.mapHoldTicks = Props.mapHoldTicks;
            missile.missileDef = usedMissile;
            if (stats != null)
            {
                missile.blastRadius = stats.blastRadius;
                missile.blastDamage = stats.blastDamage;
                missile.coreRadiusFactor = stats.coreRadiusFactor;
                missile.coreDamageFactor = stats.coreDamageFactor;
                missile.fireChance = stats.fireChance;
                missile.coreFireChance = stats.coreFireChance;
                missile.buildingDamageMultiplier = stats.buildingDamageMultiplier;
            }
            else
            {
                missile.blastRadius = Props.blastRadius;
                missile.blastDamage = Props.blastDamage;
            }
            Find.WorldObjects.Add(missile);

            StartLaunchAnimation(stats, usedMissile);

            Messages.Message(
                "Icbm_Launched".Translate(missile.ticksTotal.ToStringTicksToPeriod()).ToString(),
                new GlobalTargetInfo(tile),
                MessageTypeDefOf.PositiveEvent,
                false);
            return true;
        }
    }
}

// ===== Building_IcbmSilo.cs =====
namespace IntercontinentalMissile
{
    using System.Collections.Generic;
    using RimWorld;
    using UnityEngine;
    using Verse;

    /// <summary>
    /// Silo que muda de imagem: tampa fechada quando vazio e, quando carregado, aberto
    /// mostrando o míssil que está dentro (cada tipo de míssil pode ter a sua imagem).
    /// </summary>
    public class Building_IcbmSilo : Building
    {
        // Imagem usada quando o míssil não define a sua (loadedSiloTexture em MissileStats)
        private const string DefaultLoadedTexPath = "Things/Building/IcbmSiloLoaded";

        private readonly Dictionary<string, Graphic> loadedGraphics = new Dictionary<string, Graphic>();
        private bool shownLoaded;
        private ThingDef shownMissile;

        // Durante os primeiros instantes da decolagem o silo continua aberto, com o míssil saindo
        private bool HoldOpen
        {
            get
            {
                CompIcbm comp = GetComp<CompIcbm>();
                return comp != null && comp.HoldLoadedLook;
            }
        }

        public bool IsLoaded
        {
            get
            {
                if (HoldOpen)
                {
                    return true;
                }
                CompRefuelable fuel = GetComp<CompRefuelable>();
                return fuel != null && fuel.Fuel >= 1f;
            }
        }

        private ThingDef LoadedMissile
        {
            get
            {
                CompIcbm comp = GetComp<CompIcbm>();
                if (comp == null)
                {
                    return null;
                }
                return HoldOpen ? comp.launchingMissile : comp.loadedMissile;
            }
        }

        private string LoadedTexturePath
        {
            get
            {
                ThingDef missile = LoadedMissile;
                MissileStats stats = missile != null ? missile.GetModExtension<MissileStats>() : null;
                if (stats != null && !string.IsNullOrEmpty(stats.loadedSiloTexture))
                {
                    return stats.loadedSiloTexture;
                }
                return DefaultLoadedTexPath;
            }
        }

        public override Graphic Graphic
        {
            get
            {
                if (IsLoaded)
                {
                    string path = LoadedTexturePath;
                    Graphic graphic;
                    if (!loadedGraphics.TryGetValue(path, out graphic))
                    {
                        graphic = GraphicDatabase.Get<Graphic_Single>(
                            path,
                            ShaderDatabase.Cutout,
                            def.graphicData.drawSize,
                            Color.white);
                        loadedGraphics[path] = graphic;
                    }
                    return graphic;
                }
                return base.Graphic;
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            shownLoaded = IsLoaded;
            shownMissile = shownLoaded ? LoadedMissile : null;
        }

        /// <summary>Redesenha o silo se o estado (vazio/carregado) ou o tipo de míssil mudou.</summary>
        public void RefreshGraphicIfNeeded()
        {
            if (!Spawned)
            {
                return;
            }

            bool loaded = IsLoaded;
            ThingDef missile = loaded ? LoadedMissile : null;
            if (loaded != shownLoaded || missile != shownMissile)
            {
                shownLoaded = loaded;
                shownMissile = missile;
                Map.mapDrawer.MapMeshDirty(Position, MapMeshFlagDefOf.Things);
            }
        }
    }
}

// ===== Building_IcbmVault.cs =====
namespace IntercontinentalMissile
{
    using System.Collections.Generic;
    using RimWorld;
    using UnityEngine;
    using Verse;

    /// <summary>
    /// Cofre de mísseis que muda de imagem conforme a vida restante:
    /// normal, danificado (abaixo de 60%) e muito danificado (abaixo de 30%).
    /// </summary>
    public class Building_IcbmVault : Building_Storage, ISlotGroupParent
    {
        private const float DamagedBelow = 0.6f;
        private const float WreckedBelow = 0.3f;

        private const float WreckedScaleX = 1.031f;
        private const float WreckedScaleY = 1.117f;

        private const string NormalPath = "Things/Building/IcbmVault";
        private const string DamagedPath = "Things/Building/IcbmVaultDamaged";
        private const string WreckedPath = "Things/Building/IcbmVaultWrecked";

        private readonly Dictionary<string, Graphic> graphics = new Dictionary<string, Graphic>();
        private int shownStage = 0;

        private int Stage
        {
            get
            {
                float pct = (float)HitPoints / MaxHitPoints;
                if (pct < WreckedBelow)
                {
                    return 2;
                }
                if (pct < DamagedBelow)
                {
                    return 1;
                }
                return 0;
            }
        }

        // Só 10 células guardam mísseis (5x2 no centro do bunker), mesmo com o prédio sendo grande.
        // Reimplementamos ISlotGroupParent para o jogo usar estas células em vez do prédio inteiro.
        private List<IntVec3> slotCells;

        public new IEnumerable<IntVec3> AllSlotCells()
        {
            return AllSlotCellsList();
        }

        public new List<IntVec3> AllSlotCellsList()
        {
            if (slotCells == null)
            {
                slotCells = new List<IntVec3>();
                CellRect rect = this.OccupiedRect();
                IntVec3 c = rect.CenterCell;
                for (int dz = -1; dz <= 0; dz++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        IntVec3 cell = new IntVec3(c.x + dx, 0, c.z + dz);
                        if (rect.Contains(cell))
                        {
                            slotCells.Add(cell);
                        }
                    }
                }
            }
            return slotCells;
        }

        public override Graphic Graphic
        {
            get
            {
                int stage = Stage;
                if (stage == 0)
                {
                    return base.Graphic;
                }

                string path = stage == 1 ? DamagedPath : WreckedPath;
                Graphic graphic;
                if (!graphics.TryGetValue(path, out graphic))
                {
                    // A imagem "muito danificado" tem entulho espalhado além do corpo do prédio,
                    // então é desenhada um pouco maior para o corpo ficar do mesmo tamanho das outras.
                    Vector2 drawSize = def.graphicData.drawSize;
                    if (stage == 2)
                    {
                        drawSize = new Vector2(drawSize.x * WreckedScaleX, drawSize.y * WreckedScaleY);
                    }
                    graphic = GraphicDatabase.Get<Graphic_Single>(
                        path,
                        ShaderDatabase.Cutout,
                        drawSize,
                        Color.white);
                    graphics[path] = graphic;
                }
                return graphic;
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            shownStage = Stage;
        }

        public override void PostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.PostApplyDamage(dinfo, totalDamageDealt);
            RefreshIfNeeded();
        }

        // Também confere de tempos em tempos, para o caso de reparos
        public override void TickRare()
        {
            base.TickRare();
            RefreshIfNeeded();
        }

        private void RefreshIfNeeded()
        {
            if (!Spawned)
            {
                return;
            }

            int stage = Stage;
            if (stage != shownStage)
            {
                shownStage = stage;
                Map.mapDrawer.MapMeshDirty(Position, MapMeshFlagDefOf.Things);
            }
        }
    }
}

// ===== MissileItem.cs =====
namespace IntercontinentalMissile
{
    using RimWorld;
    using Verse;

    /// <summary>
    /// Míssil como item. Quando está guardado dentro do cofre de mísseis ele não é desenhado
    /// (fica "dentro" do bunker); fora dele aparece normalmente.
    /// </summary>
    public class MissileItem : ThingWithComps
    {
        private bool InsideVault
        {
            get
            {
                if (!Spawned)
                {
                    return false;
                }

                var things = Map.thingGrid.ThingsListAtFast(Position);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i].def.defName == "Icbm_Vault")
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public override void Print(SectionLayer layer)
        {
            if (InsideVault)
            {
                return;
            }
            base.Print(layer);
        }
    }
}

// ===== JobDrivers.cs =====
namespace IntercontinentalMissile
{
    using System.Collections.Generic;
    using RimWorld;
    using Verse;
    using Verse.AI;

    /// <summary>O colono pega um míssil e o carrega no silo (se já houver outro lá, ele é retirado antes).</summary>
    public class JobDriver_LoadMissile : JobDriver
    {
        private const TargetIndex SiloInd = TargetIndex.A;
        private const TargetIndex MissileInd = TargetIndex.B;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(SiloInd), job, 1, -1, null, errorOnFailed)
                && pawn.Reserve(job.GetTarget(MissileInd), job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(SiloInd);
            this.FailOnDestroyedNullOrForbidden(MissileInd);

            yield return Toils_Goto.GotoThing(MissileInd, PathEndMode.ClosestTouch).FailOnSomeonePhysicallyInteracting(MissileInd);
            yield return Toils_Haul.StartCarryThing(MissileInd);
            yield return Toils_Goto.GotoThing(SiloInd, PathEndMode.Touch);
            yield return Toils_General.Wait(120).FailOnDestroyedNullOrForbidden(SiloInd).WithProgressBarToilDelay(SiloInd);

            Toil load = new Toil();
            load.initAction = delegate
            {
                Pawn actor = load.actor;
                Thing silo = actor.jobs.curJob.GetTarget(SiloInd).Thing;
                Thing carried = actor.carryTracker.CarriedThing;
                if (silo == null || carried == null)
                {
                    return;
                }

                CompRefuelable fuel = silo.TryGetComp<CompRefuelable>();
                CompIcbm icbm = silo.TryGetComp<CompIcbm>();
                if (fuel == null || icbm == null)
                {
                    return;
                }

                // Troca: se já tem um míssil no silo, ele sai para o chão
                if (fuel.Fuel >= 1f)
                {
                    if (!icbm.EjectLoadedMissile())
                    {
                        return;
                    }
                }

                fuel.Refuel(new List<Thing> { carried });

                // Se por algum motivo sobrou algo na mão, solta no chão
                if (actor.carryTracker.CarriedThing != null && !actor.carryTracker.CarriedThing.Destroyed)
                {
                    Thing dropped;
                    actor.carryTracker.TryDropCarriedThing(actor.Position, ThingPlaceMode.Near, out dropped);
                }
            };
            load.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return load;
        }
    }

    /// <summary>O colono vai até o silo e retira o míssil carregado.</summary>
    public class JobDriver_UnloadMissile : JobDriver
    {
        private const TargetIndex SiloInd = TargetIndex.A;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(SiloInd), job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(SiloInd);

            yield return Toils_Goto.GotoThing(SiloInd, PathEndMode.Touch);
            yield return Toils_General.Wait(180).FailOnDestroyedNullOrForbidden(SiloInd).WithProgressBarToilDelay(SiloInd);

            Toil unload = new Toil();
            unload.initAction = delegate
            {
                Thing silo = unload.actor.jobs.curJob.GetTarget(SiloInd).Thing;
                CompIcbm icbm = silo != null ? silo.TryGetComp<CompIcbm>() : null;
                if (icbm != null)
                {
                    icbm.EjectLoadedMissile();
                }
            };
            unload.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return unload;
        }
    }
}

// ===== FloatMenuProviders.cs =====
namespace IntercontinentalMissile
{
    using RimWorld;
    using Verse;
    using Verse.AI;

    /// <summary>
    /// Opções do botão direito no silo: com um colono selecionado, escolher qual míssil carregar
    /// (padrão ou MOAB) ou retirar o míssil que está lá. O jogo registra estas classes sozinho.
    /// </summary>
    public abstract class FloatMenuOptionProvider_Silo : FloatMenuOptionProvider
    {
        protected override bool Drafted { get { return false; } }
        protected override bool Undrafted { get { return true; } }
        protected override bool Multiselect { get { return false; } }

        protected override FloatMenuOption GetSingleOptionFor(Thing clickedThing, FloatMenuContext context)
        {
            Building_IcbmSilo silo = clickedThing as Building_IcbmSilo;
            Pawn pawn = context.FirstSelectedPawn;
            if (silo == null || pawn == null || silo.Faction != Faction.OfPlayer)
            {
                return null;
            }
            return BuildOption(silo, pawn);
        }

        protected abstract FloatMenuOption BuildOption(Building_IcbmSilo silo, Pawn pawn);
    }

    public abstract class FloatMenuOptionProvider_LoadMissile : FloatMenuOptionProvider_Silo
    {
        protected abstract string MissileDefName { get; }

        protected override FloatMenuOption BuildOption(Building_IcbmSilo silo, Pawn pawn)
        {
            ThingDef missileDef = DefDatabase<ThingDef>.GetNamedSilentFail(MissileDefName);
            if (missileDef == null)
            {
                return null;
            }

            string label = "Icbm_FloatLoad".Translate(missileDef.label).ToString();
            CompIcbm icbm = silo.GetComp<CompIcbm>();
            if (icbm != null && icbm.HasMissile)
            {
                if (icbm.loadedMissile == missileDef)
                {
                    return new FloatMenuOption("Icbm_FloatAlreadyLoaded".Translate(label).ToString(), null);
                }
                label = "Icbm_FloatReplaces".Translate(label).ToString();
            }

            if (!pawn.CanReach(silo, PathEndMode.Touch, Danger.Deadly))
            {
                return new FloatMenuOption("Icbm_FloatUnreachable".Translate(label).ToString(), null);
            }

            Thing missile = GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(missileDef),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                9999f,
                t => !t.IsForbidden(pawn) && pawn.CanReserve(t));
            if (missile == null)
            {
                return new FloatMenuOption("Icbm_FloatNoneAvailable".Translate(label).ToString(), null);
            }

            return new FloatMenuOption(label, delegate
            {
                Job job = JobMaker.MakeJob(IcbmDefOf.Icbm_LoadMissile, silo, missile);
                job.count = 1;
                pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            });
        }
    }

    public class FloatMenuOptionProvider_LoadStandardMissile : FloatMenuOptionProvider_LoadMissile
    {
        protected override string MissileDefName { get { return "Icbm_Missile"; } }
    }

    public class FloatMenuOptionProvider_LoadHeavyMissile : FloatMenuOptionProvider_LoadMissile
    {
        protected override string MissileDefName { get { return "Icbm_MissileHeavy"; } }
    }

    public class FloatMenuOptionProvider_UnloadMissile : FloatMenuOptionProvider_Silo
    {
        protected override FloatMenuOption BuildOption(Building_IcbmSilo silo, Pawn pawn)
        {
            CompIcbm icbm = silo.GetComp<CompIcbm>();
            if (icbm == null || !icbm.HasMissile)
            {
                return null;
            }

            if (!pawn.CanReach(silo, PathEndMode.Touch, Danger.Deadly))
            {
                return new FloatMenuOption("Icbm_FloatUnloadUnreachable".Translate().ToString(), null);
            }

            return new FloatMenuOption("Icbm_FloatUnload".Translate().ToString(), delegate
            {
                Job job = JobMaker.MakeJob(IcbmDefOf.Icbm_UnloadMissile, silo);
                pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            });
        }
    }
}

// ===== BombedSettlementsTracker.cs =====
namespace IntercontinentalMissile
{
    using System.Collections.Generic;
    using RimWorld;
    using RimWorld.Planet;
    using Verse;

    /// <summary>
    /// Guarda quais tiles (assentamentos bombardeados) devem ter o mapa preservado,
    /// e até quando. O jogo cria este componente sozinho e salva junto com o jogo.
    /// </summary>
    public class BombedSettlementsTracker : GameComponent
    {
        private Dictionary<int, int> heldUntil = new Dictionary<int, int>();
        private List<int> tmpKeys;
        private List<int> tmpValues;

        public BombedSettlementsTracker(Game game)
        {
        }

        private static BombedSettlementsTracker Instance
        {
            get { return Current.Game != null ? Current.Game.GetComponent<BombedSettlementsTracker>() : null; }
        }

        /// <summary>Preserva o mapa deste tile pelos próximos "ticks".</summary>
        public static void Hold(int tile, int ticks)
        {
            BombedSettlementsTracker tracker = Instance;
            if (tracker != null)
            {
                tracker.heldUntil[tile] = Find.TickManager.TicksGame + ticks;
            }
        }

        public static bool IsHeld(int tile)
        {
            BombedSettlementsTracker tracker = Instance;
            if (tracker == null || tracker.heldUntil.Count == 0)
            {
                return false;
            }

            int until;
            if (!tracker.heldUntil.TryGetValue(tile, out until))
            {
                return false;
            }
            if (Find.TickManager.TicksGame < until)
            {
                return true;
            }

            tracker.heldUntil.Remove(tile);   // expirou
            return false;
        }

        /// <summary>True se há colonos/caravana do jogador no mapa deste tile.</summary>
        public static bool PlayerPresentAt(int tile)
        {
            foreach (Map map in Find.Maps)
            {
                if (map.Tile == tile && map.mapPawns.FreeColonistsSpawnedCount > 0)
                {
                    return true;
                }
            }
            return false;
        }

        // Verifica de tempos em tempos se o assentamento bombardeado já pode ser dado como derrotado:
        // quando a caravana do jogador chega, ou quando o período de preservação acaba.
        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (heldUntil.Count == 0 || Find.TickManager.TicksGame % 250 != 0)
            {
                return;
            }

            List<int> tiles = new List<int>(heldUntil.Keys);
            foreach (int tile in tiles)
            {
                Settlement settlement = null;
                foreach (WorldObject wo in Find.WorldObjects.AllWorldObjects)
                {
                    if (wo.Tile == tile && wo is Settlement s)
                    {
                        settlement = s;
                        break;
                    }
                }
                if (settlement == null)
                {
                    heldUntil.Remove(tile);
                    continue;
                }

                bool expired = Find.TickManager.TicksGame >= heldUntil[tile];
                if (expired)
                {
                    heldUntil.Remove(tile);
                }
                if ((expired || PlayerPresentAt(tile)) && settlement.HasMap)
                {
                    // Se não sobrou nenhum inimigo, o jogo derrota (remove) o assentamento normalmente
                    SettlementDefeatUtility.CheckDefeated(settlement);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref heldUntil, "heldUntil", LookMode.Value, LookMode.Value, ref tmpKeys, ref tmpValues);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && heldUntil == null)
            {
                heldUntil = new Dictionary<int, int>();
            }
        }
    }
}

// ===== HarmonyPatches.cs =====
namespace IntercontinentalMissile
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using HarmonyLib;
    using RimWorld;
    using RimWorld.Planet;
    using Verse;

    /// <summary>
    /// 1) Normalmente o jogo descarta o mapa de um assentamento quando não há colonos nele,
    ///    o que apagaria os estragos do bombardeio. O patch faz o jogo manter o mapa
    ///    dos tiles marcados pelo BombedSettlementsTracker.
    /// 2) Descobre qual tipo de míssil foi carregado no silo (o jogo só guarda "combustível").
    /// </summary>
    [StaticConstructorOnStartup]
    public static class HarmonyPatches
    {
        static HarmonyPatches()
        {
            Harmony harmony = new Harmony("gabriel.intercontinentalmissile");

            // 1) manter o mapa bombardeado
            HarmonyMethod keepPrefix = new HarmonyMethod(typeof(HarmonyPatches), nameof(KeepBombedMap));
            int patched = 0;
            foreach (Type type in typeof(MapParent).AllSubclasses().Concat(new[] { typeof(MapParent) }))
            {
                MethodInfo method = type.GetMethod(
                    "ShouldRemoveMapNow",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method == null)
                {
                    continue;
                }

                try
                {
                    harmony.Patch(method, prefix: keepPrefix);
                    patched++;
                }
                catch (Exception e)
                {
                    Log.Warning("[Intercontinental Missile] Could not apply the patch to " + type.Name + ": " + e.Message);
                }
            }
            if (patched == 0)
            {
                Log.Error("[Intercontinental Missile] No patch applied: the bombed settlement map will not be preserved.");
            }

            // 3) não "derrotar" (apagar) o assentamento quando todos morrem no bombardeio
            MethodInfo defeated = typeof(SettlementDefeatUtility).GetMethod("CheckDefeated", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (defeated != null)
            {
                try
                {
                    harmony.Patch(defeated, prefix: new HarmonyMethod(typeof(HarmonyPatches), nameof(KeepSettlementAlive)));
                }
                catch (Exception e)
                {
                    Log.Warning("[Intercontinental Missile] CheckDefeated patch failed: " + e.Message);
                }
            }
            else
            {
                Log.Warning("[Intercontinental Missile] SettlementDefeatUtility.CheckDefeated not found: the settlement may disappear if everyone dies.");
            }

            // 4) mísseis guardados no cofre reforçado não sofrem dano (fogo, explosão etc.)
            MethodInfo preDamage = typeof(Thing).GetMethod("PreApplyDamage", BindingFlags.Instance | BindingFlags.Public);
            if (preDamage != null)
            {
                try
                {
                    harmony.Patch(preDamage, prefix: new HarmonyMethod(typeof(HarmonyPatches), nameof(ProtectStoredMissile)));
                }
                catch (Exception e)
                {
                    Log.Warning("[Intercontinental Missile] Missile vault patch failed: " + e.Message);
                }
            }
            else
            {
                Log.Warning("[Intercontinental Missile] Thing.PreApplyDamage not found: the missile vault will not protect missiles.");
            }

            // 5) ao terminar de construir o cofre, o jogo tenta copiar as configurações de armazenamento
            //    da obra, que vêm vazias (null) e dão NullReferenceException. Ignora a cópia nesse caso.
            MethodInfo copyFrom = typeof(StorageSettings).GetMethod("CopyFrom", new[] { typeof(StorageSettings) });
            if (copyFrom != null)
            {
                try
                {
                    harmony.Patch(copyFrom, prefix: new HarmonyMethod(typeof(HarmonyPatches), nameof(SkipNullStorageCopy)));
                }
                catch (Exception e)
                {
                    Log.Warning("[Intercontinental Missile] StorageSettings.CopyFrom patch failed: " + e.Message);
                }
            }

            // 2) lembrar o tipo de míssil carregado
            MethodInfo refuel = typeof(CompRefuelable).GetMethod("Refuel", new[] { typeof(List<Thing>) });
            if (refuel != null)
            {
                harmony.Patch(refuel, prefix: new HarmonyMethod(typeof(HarmonyPatches), nameof(RememberLoadedMissile)));
            }
            else
            {
                Log.Warning("[Intercontinental Missile] CompRefuelable.Refuel(List<Thing>) not found: every missile will be treated as the standard one.");
            }
        }

        public static bool KeepBombedMap(MapParent __instance, ref bool __result, ref bool alsoRemoveWorldObject)
        {
            if (BombedSettlementsTracker.IsHeld(__instance.Tile))
            {
                __result = false;
                alsoRemoveWorldObject = false;
                return false;   // pula o método original
            }
            return true;
        }

        // O jogo apaga o assentamento (e registra o "tale" que deu erro) quando não resta inimigo vivo.
        // Durante o período de preservação do bombardeio, isso é pulado.
        public static bool KeepSettlementAlive(Settlement factionBase)
        {
            // Só adia a derrota enquanto ninguém do jogador chegou lá; com a caravana presente, vale a regra normal
            if (factionBase != null
                && BombedSettlementsTracker.IsHeld(factionBase.Tile)
                && !BombedSettlementsTracker.PlayerPresentAt(factionBase.Tile))
            {
                return false;
            }
            return true;
        }

        // Míssil (item) em cima de um cofre de mísseis: o dano é absorvido por completo
        public static bool ProtectStoredMissile(Thing __instance, out bool absorbed)
        {
            absorbed = false;
            if (__instance == null || !__instance.Spawned || !__instance.def.HasModExtension<MissileStats>())
            {
                return true;
            }

            List<Thing> things = __instance.Map.thingGrid.ThingsListAtFast(__instance.Position);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].def.defName == "Icbm_Vault")
                {
                    absorbed = true;
                    return false;   // pula o método original
                }
            }
            return true;
        }

        // Se a origem da cópia é null, não copia nada (o cofre mantém o filtro padrão do XML)
        public static bool SkipNullStorageCopy(StorageSettings other)
        {
            return other != null;
        }

        public static void RememberLoadedMissile(CompRefuelable __instance, List<Thing> fuelThings)
        {
            if (fuelThings == null || __instance == null || __instance.parent == null)
            {
                return;
            }

            CompIcbm silo = __instance.parent.GetComp<CompIcbm>();
            if (silo == null)
            {
                return;
            }

            foreach (Thing t in fuelThings)
            {
                if (t != null && t.def.HasModExtension<MissileStats>())
                {
                    silo.loadedMissile = t.def;
                    return;
                }
            }
        }
    }
}

// ===== MissileInFlight.cs =====
namespace IntercontinentalMissile
{
    using System.Collections.Generic;
    using RimWorld;
    using RimWorld.Planet;
    using UnityEngine;
    using Verse;

    /// <summary>
    /// Míssil em voo no mapa-múndi. Ao chegar ao assentamento alvo, o mapa dele é aberto,
    /// o jogador escolhe o ponto de impacto, o míssil cai do céu e explode.
    /// O assentamento NÃO é removido: ele fica em ruínas para ser saqueado depois.
    /// </summary>
    public class MissileInFlight : WorldObject
    {
        // Duração da queda do míssil no mapa do alvo (ticks). 150 = 2,5 segundos.
        private const int FallTicks = 150;
        // Altura inicial da queda, em células acima do ponto de impacto.
        private const float FallHeight = 40f;

        // Onda de choque: demora este tanto de ticks para varrer todo o raio (30 = 0,5 segundo).
        private const int ShockTicks = 30;
        // Fração do dano que sobra na borda do raio (1 = no centro).
        private const float EdgeFalloff = 0.25f;
        // Pessoas e itens sofrem só uma parte do dano da onda, para sobrar sobreviventes e saque.
        private const float PawnDamageFactor = 0.4f;
        private const float ItemDamageFactor = 0.3f;

        public int origin;
        public int destination;
        public int ticksTotal = 1;
        public int ticksPassed;

        // Tipo de míssil e parâmetros da explosão (vêm do silo / do item do míssil)
        public ThingDef missileDef;
        public float blastRadius = 22f;
        public int blastDamage = 350;
        public float coreRadiusFactor = 0.35f;
        public float coreDamageFactor = 2f;
        public float fireChance = 0.1f;
        public float coreFireChance = 0.3f;
        public float buildingDamageMultiplier = 2f;
        public int mapHoldTicks = 900000;

        // Chegou ao destino e está esperando o jogador escolher o ponto de impacto (salvo)
        private bool awaitingTarget;

        // Míssil caindo no mapa do alvo (salvo)
        private bool falling;
        private int fallTicksLeft;
        private int strikeMapId = -1;
        private IntVec3 strikeCell;

        // Onda de choque em andamento (salvo)
        private bool shockActive;
        private int shockIndex;

        // Estados só de execução (não salvos)
        private bool generating;
        private bool targeted;
        private Mote fallingMote;
        private List<IntVec3> shockCells;
        private int shockDestroyed;
        private int shockDamaged;
        private int shockPawnsKilled;

        public override Vector3 DrawPos
        {
            get
            {
                float t = Mathf.Clamp01((float)ticksPassed / Mathf.Max(1, ticksTotal));
                WorldGrid grid = Find.WorldGrid;
                return Vector3.Slerp(grid.GetTileCenter(origin), grid.GetTileCenter(destination), t);
            }
        }

        protected override void Tick()
        {
            base.Tick();

            if (shockActive)
            {
                ProcessShockwave();
                return;
            }

            if (falling)
            {
                fallTicksLeft--;
                if (fallTicksLeft <= 0)
                {
                    Impact();
                }
                return;
            }

            if (awaitingTarget)
            {
                // Retoma o pedido de alvo se ele sumiu (jogo carregado, janela fechada com Esc...)
                if (!generating && !Find.Targeter.IsTargeting && !Find.WindowStack.IsOpen<Dialog_MessageBox>())
                {
                    BeginStrike();
                }
                return;
            }

            ticksPassed++;
            if (ticksPassed >= ticksTotal)
            {
                awaitingTarget = true;
                BeginStrike();
            }
        }

        private Settlement FindSettlement()
        {
            foreach (WorldObject wo in Find.WorldObjects.AllWorldObjects)
            {
                if (wo != this && wo.Tile == destination && wo is Settlement s)
                {
                    return s;
                }
            }
            return null;
        }

        private Map FindStrikeMap()
        {
            foreach (Map m in Find.Maps)
            {
                if (m.uniqueID == strikeMapId)
                {
                    return m;
                }
            }
            return null;
        }

        private void BeginStrike()
        {
            if (FindSettlement() == null)
            {
                Find.LetterStack.ReceiveLetter(
                    "Icbm_LostTitle".Translate().ToString(),
                    "Icbm_LostTargetGone".Translate().ToString(),
                    LetterDefOf.NeutralEvent,
                    new GlobalTargetInfo(destination));
                Destroy();
                return;
            }

            generating = true;
            Find.TickManager.Pause();

            // Impede que o jogo descarte o mapa do assentamento enquanto ele está sendo bombardeado
            BombedSettlementsTracker.Hold(destination, mapHoldTicks);

            int tile = destination;
            LongEventHandler.QueueLongEvent(
                delegate
                {
                    Map map = GetOrGenerateMapUtility.GetOrGenerateMap(tile, Find.World.info.initialMapSize, null);
                    RevealMap(map);
                    LongEventHandler.ExecuteWhenFinished(delegate { PromptTarget(map); });
                },
                "GeneratingMapForNewEncounter",
                false,
                ex =>
                {
                    generating = false;
                    Log.Error("[Intercontinental Missile] Failed to open the target map: " + ex);
                });
        }

        // O mapa começa coberto de "névoa": revelamos tudo para o jogador ver onde atirar
        private static void RevealMap(Map map)
        {
            if (map == null || map.fogGrid == null)
            {
                return;
            }
            foreach (IntVec3 c in map.AllCells)
            {
                map.fogGrid.Unfog(c);
            }
        }

        private void PromptTarget(Map map)
        {
            generating = false;
            if (map == null || Destroyed)
            {
                return;
            }

            CameraJumper.TryJump(new GlobalTargetInfo(map.Center, map));
            Messages.Message(
                "Icbm_ChooseImpact".Translate().ToString(),
                MessageTypeDefOf.NeutralEvent,
                false);

            float radius = blastRadius;
            Find.Targeter.BeginTargeting(
                TargetingParameters.ForCell(),
                target => StartFall(map, target.Cell),
                highlightAction: target => GenDraw.DrawRadiusRing(target.Cell, radius),
                targetValidator: target => target.Cell.InBounds(map),
                mouseAttachment: TexCommand.Attack,
                actionWhenFinished: () => OnTargetingFinished(map));
        }

        // O jogador cancelou (botão direito/Esc) sem escolher: pergunta o que fazer
        private void OnTargetingFinished(Map map)
        {
            if (targeted || Destroyed)
            {
                return;
            }

            Find.WindowStack.Add(new Dialog_MessageBox(
                "Icbm_NoImpactChosen".Translate().ToString(),
                "Icbm_ChoosePoint".Translate().ToString(),
                () => { if (!targeted && !Destroyed) PromptTarget(map); },
                "Icbm_HitCenter".Translate().ToString(),
                () => StartFall(map, map.Center)));
        }

        // O jogador escolheu o ponto: o míssil começa a cair
        private void StartFall(Map map, IntVec3 cell)
        {
            if (targeted || Destroyed)
            {
                return;
            }
            targeted = true;
            awaitingTarget = false;
            falling = true;
            fallTicksLeft = FallTicks;
            strikeMapId = map.uniqueID;
            strikeCell = cell;

            SpawnFallingMissile(map, cell);

            // Deixa o tempo correr para a queda acontecer
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
        }

        // Animação: o míssil desce do alto da tela até o ponto de impacto
        private void SpawnFallingMissile(Map map, IntVec3 cell)
        {
            MissileStats stats = missileDef != null ? missileDef.GetModExtension<MissileStats>() : null;
            ThingDef moteDef = stats != null ? stats.fallingMote : null;
            if (moteDef == null)
            {
                moteDef = DefDatabase<ThingDef>.GetNamedSilentFail("Icbm_FallingMote");
            }
            if (moteDef == null)
            {
                return;
            }

            float scale = stats != null ? stats.fallingScale : 5f;
            float seconds = FallTicks / 60f;

            MoteThrown mote = ThingMaker.MakeThing(moteDef) as MoteThrown;
            if (mote == null)
            {
                return;
            }

            // O centro do míssil termina meio comprimento acima do ponto, para a ponta tocar o solo
            mote.exactPosition = cell.ToVector3Shifted() + new Vector3(0f, 0f, FallHeight + scale * 0.5f);
            mote.Scale = scale;
            mote.SetVelocity(180f, FallHeight / seconds);   // 180° = para baixo na tela
            GenSpawn.Spawn(mote, cell, map);
            fallingMote = mote;
        }

        // O míssil tocou o solo: explosão gigante + onda de choque
        private void Impact()
        {
            falling = false;

            if (fallingMote != null && !fallingMote.Destroyed)
            {
                fallingMote.Destroy();
            }
            fallingMote = null;

            Map map = FindStrikeMap();
            if (map == null)
            {
                Find.LetterStack.ReceiveLetter(
                    "Icbm_LostTitle".Translate().ToString(),
                    "Icbm_LostMapGone".Translate().ToString(),
                    LetterDefOf.NeutralEvent,
                    new GlobalTargetInfo(destination));
                Destroy();
                return;
            }

            IntVec3 cell = strikeCell;
            Settlement settlement = map.Parent as Settlement;
            Faction faction = settlement != null ? settlement.Faction : null;

            // Explosão visual (clarão, fogo, som). O jogo limita o alcance dela à linha de visão do
            // centro, por isso o dano em toda a área vem da onda de choque logo abaixo.
            GenExplosion.DoExplosion(
                cell, map, blastRadius, DamageDefOf.Bomb, null,
                damAmount: blastDamage,
                chanceToStartFire: fireChance,
                damageFalloff: true);
            GenExplosion.DoExplosion(
                cell, map, blastRadius * coreRadiusFactor, DamageDefOf.Bomb, null,
                damAmount: Mathf.RoundToInt(blastDamage * coreDamageFactor),
                chanceToStartFire: coreFireChance,
                damageFalloff: true);

            if (faction != null && faction != Faction.OfPlayer && !faction.def.hidden && !faction.HostileTo(Faction.OfPlayer))
            {
                faction.TryAffectGoodwillWith(Faction.OfPlayer, -80);
            }

            // Mantém o mapa (com os estragos) disponível por alguns dias para saque e prisioneiros
            BombedSettlementsTracker.Hold(destination, mapHoldTicks);

            // Onda de choque: varre o raio inteiro, de dentro para fora, atravessando paredes
            shockActive = true;
            shockIndex = 0;
            shockCells = null;
            shockDestroyed = 0;
            shockDamaged = 0;
            shockPawnsKilled = 0;
        }

        private void ProcessShockwave()
        {
            Map map = FindStrikeMap();
            if (map == null)
            {
                FinishStrike(null);
                return;
            }

            if (shockCells == null)
            {
                // Células do raio, da mais próxima do centro para a mais distante
                shockCells = new List<IntVec3>(GenRadial.RadialCellsAround(strikeCell, blastRadius, true));
            }

            int perTick = Mathf.Max(1, Mathf.CeilToInt(shockCells.Count / (float)ShockTicks));
            int end = Mathf.Min(shockCells.Count, shockIndex + perTick);
            for (; shockIndex < end; shockIndex++)
            {
                ShockCell(map, shockCells[shockIndex]);
            }

            if (shockIndex >= shockCells.Count)
            {
                FinishStrike(map);
            }
        }

        // Dano direto a tudo o que está na célula, sem depender de linha de visão
        private void ShockCell(Map map, IntVec3 c)
        {
            if (!c.InBounds(map))
            {
                return;
            }

            float dist = c.DistanceTo(strikeCell);
            float t = Mathf.Clamp01(dist / Mathf.Max(1f, blastRadius));
            float falloff = Mathf.Lerp(1f, EdgeFalloff, t);
            if (dist <= blastRadius * coreRadiusFactor)
            {
                falloff *= coreDamageFactor;
            }
            float baseDamage = blastDamage * falloff;

            // Cópia da lista: destruir coisas altera a lista original
            List<Thing> things = new List<Thing>(c.GetThingList(map));
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing == null || thing.Destroyed || !thing.Spawned)
                {
                    continue;
                }

                switch (thing.def.category)
                {
                    case ThingCategory.Building:
                        if (thing.def.mineable)
                        {
                            break;   // rocha natural não é afetada
                        }
                        DamageDirectly(thing, Mathf.RoundToInt(baseDamage * buildingDamageMultiplier), true);
                        break;

                    case ThingCategory.Item:
                        DamageDirectly(thing, Mathf.RoundToInt(baseDamage * ItemDamageFactor), false);
                        break;

                    case ThingCategory.Pawn:
                        int pawnDamage = Mathf.RoundToInt(baseDamage * PawnDamageFactor);
                        if (pawnDamage > 0)
                        {
                            thing.TakeDamage(new DamageInfo(DamageDefOf.Bomb, pawnDamage, 0.5f));
                            Pawn pawn = thing as Pawn;
                            if (pawn != null && pawn.Dead)
                            {
                                shockPawnsKilled++;
                            }
                        }
                        break;
                }
            }
        }

        // Tira pontos de vida direto (o dano de explosão do jogo é reduzido contra construções)
        private void DamageDirectly(Thing thing, int amount, bool isBuilding)
        {
            if (amount <= 0 || !thing.def.useHitPoints)
            {
                return;
            }

            int hp = thing.HitPoints;
            if (amount >= hp)
            {
                thing.Kill();
                if (isBuilding)
                {
                    shockDestroyed++;
                }
            }
            else
            {
                thing.HitPoints = hp - amount;
                if (isBuilding)
                {
                    shockDamaged++;
                }
            }
        }

        private void FinishStrike(Map map)
        {
            shockActive = false;
            shockCells = null;

            string label = "Icbm_TheTarget".Translate().ToString();
            GlobalTargetInfo look = new GlobalTargetInfo(destination);
            if (map != null)
            {
                Settlement settlement = map.Parent as Settlement;
                if (settlement != null)
                {
                    label = settlement.LabelCap.ToString();
                }
                look = new GlobalTargetInfo(strikeCell, map);
            }

            Log.Message("[Intercontinental Missile] Impact at " + label + ": " + shockDestroyed + " buildings destroyed, "
                + shockDamaged + " damaged, " + shockPawnsKilled + " people killed by the shockwave (radius " + blastRadius + ").");

            int days = Mathf.RoundToInt(mapHoldTicks / 60000f);
            Find.LetterStack.ReceiveLetter(
                "Icbm_ImpactTitle".Translate().ToString(),
                "Icbm_ImpactText".Translate(label, shockDestroyed, shockDamaged, days).ToString(),
                LetterDefOf.NeutralEvent,
                look);

            Destroy();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref origin, "origin");
            Scribe_Values.Look(ref destination, "destination");
            Scribe_Values.Look(ref ticksTotal, "ticksTotal", 1);
            Scribe_Values.Look(ref ticksPassed, "ticksPassed");
            Scribe_Defs.Look(ref missileDef, "missileDef");
            Scribe_Values.Look(ref blastRadius, "blastRadius", 22f);
            Scribe_Values.Look(ref blastDamage, "blastDamage", 350);
            Scribe_Values.Look(ref coreRadiusFactor, "coreRadiusFactor", 0.35f);
            Scribe_Values.Look(ref coreDamageFactor, "coreDamageFactor", 2f);
            Scribe_Values.Look(ref fireChance, "fireChance", 0.1f);
            Scribe_Values.Look(ref coreFireChance, "coreFireChance", 0.3f);
            Scribe_Values.Look(ref buildingDamageMultiplier, "buildingDamageMultiplier", 2f);
            Scribe_Values.Look(ref mapHoldTicks, "mapHoldTicks", 900000);
            Scribe_Values.Look(ref awaitingTarget, "awaitingTarget");
            Scribe_Values.Look(ref falling, "falling");
            Scribe_Values.Look(ref fallTicksLeft, "fallTicksLeft");
            Scribe_Values.Look(ref strikeMapId, "strikeMapId", -1);
            Scribe_Values.Look(ref strikeCell, "strikeCell");
            Scribe_Values.Look(ref shockActive, "shockActive");
            Scribe_Values.Look(ref shockIndex, "shockIndex");
        }
    }
}
