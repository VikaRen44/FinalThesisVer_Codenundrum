// ISaveable.cs
public interface ISaveable
{
    // Return a serializable string (usually JSON) of your state
    string CaptureState();

    // Receive that string back to restore
    void RestoreState(string state);
}
