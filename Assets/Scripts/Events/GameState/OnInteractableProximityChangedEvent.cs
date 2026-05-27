/// <summary>
/// Raised by Interactor whenever its "closest interactable" pointer transitions
/// between null and non-null. Lets UI react to "there's something to interact
/// with" without polling Interactor.onInteractArea every frame.
/// </summary>
public class OnInteractableProximityChangedEvent
{
    public bool nearby;
}
