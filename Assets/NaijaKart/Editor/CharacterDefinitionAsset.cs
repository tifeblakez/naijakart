using System.IO;
using NaijaKart.Core.Config;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace NaijaKart.Editor
{
    [CreateAssetMenu(menuName = "Naija Kart/Content/Character Definition")]
    public sealed class CharacterDefinitionAsset : ScriptableObject { public CharacterDefinition definition = new CharacterDefinition(); }
}
