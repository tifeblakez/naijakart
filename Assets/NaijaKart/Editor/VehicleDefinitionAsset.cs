using System.IO;
using NaijaKart.Core.Config;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace NaijaKart.Editor
{
    [CreateAssetMenu(menuName = "Naija Kart/Content/Vehicle Definition")]
    public sealed class VehicleDefinitionAsset : ScriptableObject { public VehicleDefinition definition = new VehicleDefinition(); }
}
