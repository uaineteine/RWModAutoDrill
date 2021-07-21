using RimWorld;
using UnityEngine;
//using System.Linq;
using System.Collections.Generic;
using Verse;

// written by Alaestor Weissman a.k.a. Aion Algos (alaestor@null.net)
// most of this code is copied and modified from vanilla. I'm a simple man of simple C/C++ means; forgive my C# sins

namespace AutoDeepDrill
{
	public class CompProperties_AutoDeepDrill : CompProperties
	{
		public bool consumeDeepResources = true;
		public float resourceConsumptionMultiplier = 1.0f;
		public float resourceOutputMultiplier = 1.0f;
		public IntRange stoneChunkQuantity = new IntRange(1, 1);
		public IntRange spawnIntervalRange = new IntRange(2500, 2500);
		public string saveKeysPrefix; // ? idk what this is but I'll leave it alone just to be safe
		/* could add fixed quantity and item, but wont for simplicity
		public ThingDef thingToSpawn = null; // if not null, item type will spawn
		public int quantityToSpawn = 0; // if >0, will use instead of scaled resPortion
		*/
		
		public CompProperties_AutoDeepDrill()
		{
			compClass = typeof(CompAutoDeepDrill);
		}
		
		public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
		{
			foreach (string item in base.ConfigErrors(parentDef))
			{
				yield return item;
			}
			if (parentDef.specialDisplayRadius > 4)
			{
				yield return "specialDisplayRadius should be less than or equal to four (4) for AutoDrill to behave properly. Limited by hardcoded loop value in private void FlickOffExhaustedDrillsAroundParent()";
			}
			if (resourceConsumptionMultiplier < 0.0f)
			{
				yield return "resourceConsumptionMultiplier should be greater than or equal to zero (0) to behave properly. If you want to disable resource consumption and depletion, set property \"consumeDeepResources\" to false.";
			}
			if (resourceOutputMultiplier <= 0.0f)
			{
				yield return "resourceMultiplier must be greater than zero (0) to behave properly.";
			}
		}
	}

	public class CompAutoDeepDrill : ThingComp
	{ // The following is a modified hybrid of "RimWorld.CompSpawner" and "RimWorld.CompDeepDrill"
		private int ticksUntilSpawn;
		private bool PowerOn => parent.GetComp<CompPowerTrader>()?.PowerOn ?? false;
		public CompProperties_AutoDeepDrill parentSettings => (CompProperties_AutoDeepDrill)props;
		
		private bool GetNextResource(out ThingDef resDef, out int countPresent, out IntVec3 cell)
		{
			for (int i = 0; i < GenRadial.NumCellsInRadius(Mathf.Max(0, parent.def.specialDisplayRadius)); ++i)
			{
				IntVec3 intVec = parent.Position + GenRadial.RadialPattern[i];
				if (intVec.InBounds(parent.Map))
				{
					ThingDef thingDef = parent.Map.deepResourceGrid.ThingDefAt(intVec);
					if (thingDef != null)
					{
						resDef = thingDef;
						countPresent = parent.Map.deepResourceGrid.CountAt(intVec);
						cell = intVec;
						return true;
					}
				}
			}
			resDef = DeepDrillUtility.GetBaseResource(parent.Map);
			countPresent = int.MaxValue;
			cell = parent.Position;
			return false;
		}
		
		public bool ValuableResourcesPresent()
		{
			return GetNextResource(out _, out _, out _);
		}
		
		private void FlickOffExhaustedDrillsAroundParent(IntVec3 cell)
		{
			//parent.GetComp<CompFlickable>().SwitchIsOn = false; // this line would flick off parent directly, but not other drills which may have also been exhausted by parent.
			//replacing for(radial) with the above line would remove the 4 radius limit, but wont stop overlapping drills. Would be much simpler though...
			for (int i = 0; i < GenRadial.NumCellsInRadius(4 /*Mathf.Max(1,parent.def.specialDisplayRadius)*/); ++i)
			{// for drills within radius
				IntVec3 c = cell + GenRadial.RadialPattern[i];
				if (c.InBounds(parent.Map))
				{
					ThingWithComps firstThingWithComp = c.GetFirstThingWithComp<CompAutoDeepDrill>(parent.Map);
					if (firstThingWithComp != null && !firstThingWithComp.GetComp<CompAutoDeepDrill>().ValuableResourcesPresent())
					{// exhausted, no more valuable (non-chunk) resources
						firstThingWithComp.GetComp<CompFlickable>().SwitchIsOn = false; // flick since can't forbid
					}
				}
			}
		}
		
		private void FlickCheckResourceExhaustion(IntVec3 cell)
		{
			if (!ValuableResourcesPresent())
			{ // if resource tiles are depleted
				if (DeepDrillUtility.GetBaseResource(parent.Map) == null)
				{ // no fallback resource (like rock chunks)
					Messages.Message("DeepDrillExhaustedNoFallback".Translate(), parent, MessageTypeDefOf.TaskCompletion);
				}
				else
				{
					Messages.Message("DeepDrillExhausted".Translate(Find.ActiveLanguageWorker.Pluralize(DeepDrillUtility.GetBaseResource(parent.Map).label)), parent, MessageTypeDefOf.TaskCompletion);
				}
				FlickOffExhaustedDrillsAroundParent(cell);
			}
		}
		
		private void SpawnStoneChunks(ThingDef resDef)
		{
			int numberToSpawn = parentSettings.stoneChunkQuantity.RandomInRange;
			if (numberToSpawn < 1)
			{
				Messages.Message("DeepDrillExhausted".Translate(), parent, MessageTypeDefOf.TaskCompletion);
				return;
			}
			for (int i = 0; i < numberToSpawn; ++i)
			{ // try to spawn stone chunks
				Thing thingToSpawn = ThingMaker.MakeThing(resDef);
				thingToSpawn.stackCount = 1;
				GenPlace.TryPlaceThing(thingToSpawn, parent.Position, parent.Map, ThingPlaceMode.Near);
			}
		}
		
		private void SpawnValuableResource(ThingDef resDef, int countPresent, IntVec3 cell)
		{
			int resPortion = Mathf.Min(countPresent, resDef.deepCountPerPortion);
			if (parentSettings.consumeDeepResources)
			{ // decrease remaining tile resource quantity
				parent.Map.deepResourceGrid.SetAt(cell, resDef, Mathf.Max(0, countPresent - GenMath.RoundRandom(resPortion * parentSettings.resourceConsumptionMultiplier)));
			}
			Thing thingToSpawn = ThingMaker.MakeThing(resDef); // make thing determined by deep resource definition
			thingToSpawn.stackCount = Mathf.Max(1, GenMath.RoundRandom((float)resPortion * Find.Storyteller.difficulty.mineYieldFactor * parentSettings.resourceOutputMultiplier)); // set spawn quantity
			GenPlace.TryPlaceThing(thingToSpawn, parent.Position, parent.Map, ThingPlaceMode.Near); // spawn
		}
		
		public void TrySpawn()
		{
			bool nonStoneChunkResource = GetNextResource(out ThingDef resDef, out int countPresent, out IntVec3 cell);
			if (resDef == null) return; // no deep resource found, TODO should probably error
			if (nonStoneChunkResource)
			{
				SpawnValuableResource(resDef, countPresent, cell);
				FlickCheckResourceExhaustion(cell);
			}
			else SpawnStoneChunks(resDef);
			return;
		}
		
		public override void PostSpawnSetup(bool respawningAfterLoad)
		{
			if (!respawningAfterLoad) ResetTimer();
		}
		
		public override void PostExposeData()
		{
			string str = (!parentSettings.saveKeysPrefix.NullOrEmpty()) ? (parentSettings.saveKeysPrefix + "_") : null; // I  have no idea what this is for
			Scribe_Values.Look(ref ticksUntilSpawn, str + "ticksUntilSpawn", 0); // save state
		}
		
		public void ResetTimer()
		{
			ticksUntilSpawn = parentSettings.spawnIntervalRange.RandomInRange;
		}
		
		
		private bool CanDrillNow()
		{
			if (!parent.Spawned || !PowerOn) return false;
			if (DeepDrillUtility.GetBaseResource(parent.Map) != null) return true; // this makes many future checks redundant; why, tynan?
			return ValuableResourcesPresent();
		}


		public override void CompTick()
		{ TickInterval(1); }

		public override void CompTickRare()
		{ TickInterval(250); }

		private void TickInterval(int interval)
		{
			if (CanDrillNow())
			{
				ticksUntilSpawn -= interval;
				if (ticksUntilSpawn <= 0)
				{
					TrySpawn();
					ResetTimer();
				}
			}
		}
		
		public override IEnumerable<Gizmo> CompGetGizmosExtra()
		{
			if (Prefs.DevMode)
			{
				GetNextResource(out ThingDef resDef, out _, out _);
				if (resDef == null)
				{
					yield return new Command_Action
					{
						defaultLabel = "DEBUG: " + "DeepDrillNoResources".Translate(),
						action = delegate {} // nothing to do
					};
				}
				else
				{
					yield return new Command_Action
					{
						defaultLabel = "DEBUG: Drill " + resDef.label,
						action = delegate
						{
							TrySpawn();
							ResetTimer();
						}
					};
				}
			}
		}
		
		public override string CompInspectStringExtra()
		{
			if (parent.Spawned && PowerOn)
			{
				GetNextResource(out ThingDef resDef, out _, out _);
				if (resDef == null)
				{
					return "DeepDrillNoResources".Translate();
				}
				else return "ResourceBelow".Translate() + ": " + resDef.LabelCap + "\n" + "NextSpawnedItemIn".Translate(resDef.label) + " " + ticksUntilSpawn.ToStringTicksToPeriod();
			}
			else return null;
		}
	}
}
