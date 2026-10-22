using Faryhasiki.PlaySessions;
using UnityEngine.InputSystem;

[assembly: PlaySessionAdapter(typeof(PlaySessionDemo.InputSystemAdapter))]
[assembly: PlaySessionCompatibility(DependencyKind.Package, "com.unity.inputsystem", "1.20.1",
    DependencyHandling.SelfManaged,
    "InputSystemEditorInitializer.OnGlobalInitialize/OnPlayModeChanged handles package state. Project-owned actions still require disposal; see InputSystemAdapter and repeated Play tests.")]

namespace PlaySessionDemo
{
    [SessionOwned("demo.input")]
    public static class DemoInput
    {
        public static InputAction Action;
        public static int PerformedCount;
    }

    // Separate project integration; never touches PackageCache or Input System internals.
    public sealed class InputSystemAdapter : IPlaySessionAdapter
    {
        public void Configure(SessionPlan plan)
        {
            plan.Add("demo.input", _ => { DemoInput.Action = null; DemoInput.PerformedCount = 0; }, scope =>
            {
                var action = scope.Own(new InputAction("DemoJump", InputActionType.Button, "<Keyboard>/space"));
                DemoInput.Action = action;
                System.Action<InputAction.CallbackContext> handler = _ => scope.TryCommit(() => DemoInput.PerformedCount++);
                action.performed += handler;
                scope.OnStop(() => { action.Disable(); action.performed -= handler; DemoInput.Action = null; });
                action.Enable();
            });
        }
    }
}
