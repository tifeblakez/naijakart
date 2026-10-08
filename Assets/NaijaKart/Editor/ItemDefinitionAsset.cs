using System.IO;
using NaijaKart.Core.Config;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace NaijaKart.Editor
{
    [CreateAssetMenu(menuName = "Naija Kart/Content/Item Definition")]
    public sealed class ItemDefinitionAsset : ScriptableObject { public ItemDefinition definition = new ItemDefinition(); }
}
