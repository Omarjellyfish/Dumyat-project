using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>Published by <see cref="CraftingManager"/> when a blueprint/recipe is unlocked.</summary>
    public readonly struct BlueprintUnlockedEvent : IGameEvent
    {
        public readonly string RecipeName;

        public BlueprintUnlockedEvent(string recipeName)
        {
            RecipeName = recipeName;
        }
    }

    /// <summary>Published by <see cref="CraftingValidator"/> when a build validation finishes.</summary>
    public readonly struct CraftingValidatedEvent : IGameEvent
    {
        public readonly ValidationResult Result;

        public CraftingValidatedEvent(ValidationResult result)
        {
            Result = result;
        }
    }

    /// <summary>Published by <see cref="CraftingStation"/> when a recipe is selected and its root spawned.</summary>
    public readonly struct RecipeSelectedEvent : IGameEvent
    {
        public readonly CraftingRecipe Recipe;
        public readonly CraftingStation Station;

        public RecipeSelectedEvent(CraftingRecipe recipe, CraftingStation station)
        {
            Recipe = recipe;
            Station = station;
        }
    }

    /// <summary>Published by <see cref="NailHammer"/> when two structural pieces are nailed together.</summary>
    public readonly struct NailAttachedEvent : IGameEvent
    {
        public readonly Vector3 Point;
        public readonly GameObject BodyA;
        public readonly GameObject BodyB;

        public NailAttachedEvent(Vector3 point, GameObject bodyA, GameObject bodyB)
        {
            Point = point;
            BodyA = bodyA;
            BodyB = bodyB;
        }
    }

    /// <summary>Published by <see cref="NailHammer"/> when a nail joint between two pieces is removed.</summary>
    public readonly struct NailRemovedEvent : IGameEvent
    {
        public readonly Vector3 Point;
        public readonly GameObject BodyA;
        public readonly GameObject BodyB;

        public NailRemovedEvent(Vector3 point, GameObject bodyA, GameObject bodyB)
        {
            Point = point;
            BodyA = bodyA;
            BodyB = bodyB;
        }
    }

    /// <summary>Published by <see cref="CrowbarTool"/> when an attachment between two pieces is pried apart.</summary>
    public readonly struct AttachmentRemovedEvent : IGameEvent
    {
        public readonly Vector3 Point;
        public readonly GameObject BodyA;
        public readonly GameObject BodyB;

        public AttachmentRemovedEvent(Vector3 point, GameObject bodyA, GameObject bodyB)
        {
            Point = point;
            BodyA = bodyA;
            BodyB = bodyB;
        }
    }

    /// <summary>Published by <see cref="GlueBottle"/> when glue is applied to a structural piece.</summary>
    public readonly struct GlueAppliedEvent : IGameEvent
    {
        public readonly Vector3 Point;
        public readonly GameObject Target;

        public GlueAppliedEvent(Vector3 point, GameObject target)
        {
            Point = point;
            Target = target;
        }
    }
}
