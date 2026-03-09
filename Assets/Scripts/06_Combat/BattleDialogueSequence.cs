using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Battle/Dialogue Sequence")]
public class BattleDialogueSequence : ScriptableObject
{
    [TextArea] public List<string> lines = new List<string>();
}