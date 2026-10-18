using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Nasledie.EditorTools
{
    public sealed class ArtAssetImporter : AssetPostprocessor
    {
        private static readonly string[] BuildingNames =
        {
            "Building_Granary", "Building_House", "Building_ResidentialQuarter", "Building_Workshop",
            "Building_School", "Building_Market", "Building_Clinic", "Building_CulturalHall",
            "Building_Council", "Building_Administration", "Building_RoadServices", "Building_University",
            "Building_Hospital", "Building_ArchiveMuseum", "Building_ExchangeHouse", "Building_Barracks"
        };

        private static readonly string[] WorldNames =
        {
            "Terrain_Plains", "Terrain_Meadow", "Terrain_Forest", "Terrain_Hills",
            "Terrain_Mountain", "Terrain_River", "Terrain_Coast", "Terrain_Arid",
            "Terrain_Swamp", "Terrain_Water", "Resource_Iron", "Resource_Herbs",
            "Resource_Salt", "Resource_FertileSoil", "Improvement_Road", "Improvement_Bridge"
        };

        private static readonly string[] UnitNames =
        {
            "Unit_Militia", "Unit_FieldForce", "Unit_SiegeTeam", "Unit_Settler"
        };

        private static readonly string[] Eras =
        {
            "Communities", "Cities", "ConnectedLands", "Industry", "Synthesis"
        };

        private static readonly string[] UnitKinds =
        {
            "Militia", "FieldForce", "SiegeTeam", "Settler"
        };

        private static readonly string[] UnitClasses =
        {
            "Infantry", "Ranged", "Cavalry", "Siege", "Scout"
        };

        private static readonly string[] AchievementNames =
        {
            "Badge_Scroll", "Badge_Wheat", "Badge_City", "Badge_Book",
            "Badge_Handshake", "Badge_Heart", "Badge_Wreath", "Badge_WhiteFlag",
            "Badge_Crown", "Badge_OliveSun", "Badge_Lyre", "Badge_Star"
        };

        private static readonly string[] RuralActivityNames =
        {
            "Activity_Farmer", "Activity_Lumberjack", "Activity_Blacksmith", "Activity_Scholar"
        };

        private static readonly string[] CivicActivityNames =
        {
            "Activity_Merchant", "Activity_Artist", "Activity_Steward", "Activity_Builder"
        };

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Game/Runtime/Client/Resources/Art/")) return;
            TextureImporter importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.spritePixelsPerUnit = 100;

            if (assetPath.EndsWith("BuildingAtlas.png", StringComparison.OrdinalIgnoreCase))
                SetAtlas(importer, BuildingNames, 4, 4);
            else if (TryGetBuildingEra(assetPath, out string buildingEra))
                SetAtlas(importer, EpochNames("Building_", buildingEra, BuildingNames), 4, 4);
            else if (assetPath.EndsWith("WorldAtlas.png", StringComparison.OrdinalIgnoreCase))
                SetAtlas(importer, WorldNames, 4, 4);
            else if (assetPath.EndsWith("UnitAtlas.png", StringComparison.OrdinalIgnoreCase))
                SetAtlas(importer, UnitNames, 2, 2);
            else if (assetPath.EndsWith("UnitEraAtlas.png", StringComparison.OrdinalIgnoreCase))
                SetAtlas(importer, EpochUnitNames(), 4, 5);
            else if (assetPath.EndsWith("UnitRosterAtlas.png", StringComparison.OrdinalIgnoreCase))
                SetAtlas(importer, RosterUnitNames(), 5, 5);
            else if (assetPath.EndsWith("AchievementAtlas.png", StringComparison.OrdinalIgnoreCase))
                SetAtlas(importer, AchievementNames, 4, 3);
            else if (assetPath.EndsWith("WorkerActivities_Rural.png", StringComparison.OrdinalIgnoreCase))
                SetActivityAtlas(importer, RuralActivityNames);
            else if (assetPath.EndsWith("WorkerActivities_Civic.png", StringComparison.OrdinalIgnoreCase))
                SetActivityAtlas(importer, CivicActivityNames);
            else importer.spriteImportMode = SpriteImportMode.Single;
        }

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/Game/Runtime/Client/Resources/Audio/")) return;
            AudioImporter importer = (AudioImporter)assetImporter;
            bool effect = assetPath.Contains("/Audio/Sfx/");
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = effect ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.CompressedInMemory;
            settings.compressionFormat = effect ? AudioCompressionFormat.PCM : AudioCompressionFormat.Vorbis;
            settings.quality = effect ? 1f : 0.72f;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = false;
        }

        private static bool TryGetBuildingEra(string path, out string era)
        {
            for (int i = 0; i < Eras.Length; i++)
            {
                if (path.EndsWith("BuildingAtlas_" + Eras[i] + ".png", StringComparison.OrdinalIgnoreCase))
                {
                    era = Eras[i];
                    return true;
                }
            }
            era = null;
            return false;
        }

        private static string[] EpochNames(string prefix, string era, string[] names)
        {
            var result = new string[names.Length];
            for (int i = 0; i < names.Length; i++)
                result[i] = prefix + era + "_" + names[i].Substring(prefix.Length);
            return result;
        }

        private static string[] EpochUnitNames()
        {
            var result = new string[Eras.Length * UnitKinds.Length];
            int index = 0;
            for (int era = 0; era < Eras.Length; era++)
                for (int unit = 0; unit < UnitKinds.Length; unit++)
                    result[index++] = "Unit_" + Eras[era] + "_" + UnitKinds[unit];
            return result;
        }

        private static string[] RosterUnitNames()
        {
            var result = new string[Eras.Length * UnitClasses.Length];
            int index = 0;
            for (int era = 0; era < Eras.Length; era++)
                for (int unitClass = 0; unitClass < UnitClasses.Length; unitClass++)
                    result[index++] = "UnitRoster_" + Eras[era] + "_" + UnitClasses[unitClass];
            return result;
        }

        private void SetAtlas(TextureImporter importer, string[] names, int columns, int rows)
        {
            byte[] png = File.ReadAllBytes(assetPath);
            int width = ReadPngDimension(png, 16);
            int height = ReadPngDimension(png, 20);
            if (width <= 0 || height <= 0) { importer.spriteImportMode = SpriteImportMode.Single; return; }
            var sprites = new SpriteMetaData[names.Length];
            for (int row = 0; row < rows; row++)
                for (int col = 0; col < columns; col++)
                {
                    int index = row * columns + col;
                    sprites[index].name = names[index];
                    int left = col * width / columns;
                    int right = (col + 1) * width / columns;
                    int bottom = (rows - row - 1) * height / rows;
                    int top = (rows - row) * height / rows;
                    sprites[index].rect = new Rect(left, bottom, right - left, top - bottom);
                    sprites[index].alignment = (int)SpriteAlignment.Center;
                    sprites[index].pivot = new Vector2(0.5f, 0.5f);
                }
            importer.spriteImportMode = SpriteImportMode.Multiple;
#pragma warning disable 618
            importer.spritesheet = sprites;
#pragma warning restore 618
        }

        private void SetActivityAtlas(TextureImporter importer, string[] activityNames)
        {
            string[] names = new string[activityNames.Length * 4];
            for (int activity = 0; activity < activityNames.Length; activity++)
                for (int frame = 0; frame < 4; frame++)
                    names[activity * 4 + frame] = activityNames[activity] + "_" + frame;
            SetAtlas(importer, names, 4, 4);
        }

        private static int ReadPngDimension(byte[] bytes, int offset)
        {
            if (bytes == null || bytes.Length < offset + 4) return 0;
            return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
        }
    }
}
