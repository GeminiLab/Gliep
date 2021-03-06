using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using GeminiLab.Glos;
using GeminiLab.Glug;

namespace GeminiLab.Gliep {
    public class LoadedUnit {
        public string     Key      { get; }
        public IFileInfo? Location { get; }
        public IGlosUnit  Unit     { get; }
        public GlosValue  Result   { get; }

        public LoadedUnit(string key, IFileInfo location, IGlosUnit unit, GlosValue result) {
            Key = key;
            Location = location;
            Unit = unit;
            Result = result;
        }
    }

    public class UnitManager {
        private readonly Dictionary<string, LoadedUnit>    _units         = new Dictionary<string, LoadedUnit>();
        private readonly Dictionary<IGlosUnit, IFileInfo?> _unit2Location = new Dictionary<IGlosUnit, IFileInfo?>();

        private IFileSystem _fs;
        private GlosViMa    _viMa;

        private readonly GlosContext _global;

        public UnitManager(IFileSystem fs, GlosViMa viMa, GlosContext global) {
            _fs = fs;
            _viMa = viMa;
            _global = global;
        }

        public void ParseRequireKey(string key, string cwd, out bool isLibraryRequire, out string? fileLocation) {
            if (key[0] != '@') {
                isLibraryRequire = false;
                fileLocation = Path.Combine(cwd, key);
            } else {
                isLibraryRequire = true;
                fileLocation = null;
            }
        }

        public bool Loaded(string key) => _units.ContainsKey(key);

        public LoadedUnit Load(string key, GlosCoroutine cor) {
            if (Loaded(key)) return _units[key];

            var ceu = cor.CurrentExecutingUnit;
            var currentLocation = _unit2Location[ceu]?.Directory.FullName ?? DefaultWorkingDirectory;

            ParseRequireKey(key, currentLocation, out var isLibrary, out string? file);

            if (isLibrary) throw new NotImplementedException();

            using var sr = new StreamReader(new FileStream(file!, FileMode.Open, FileAccess.Read));

            var unit = TypicalCompiler.Compile(sr);
            var location = _fs.FileInfo.FromFileName(file!);

            _unit2Location[unit] = location;

            var result = cor.ExecuteFunctionSync(new GlosFunction(unit.FunctionTable[unit.Entry], _global, unit));
            var loaded = new LoadedUnit(key, location, unit, result.Length > 0 ? result[0] : GlosValue.NewNil());
            return _units[key] = loaded;
        }

        public void AddUnit(LoadedUnit unit) {
            _units[unit.Key] = unit;
            _unit2Location[unit.Unit] = unit.Location;
        }

        public void Unload(string key) {
            if (Loaded(key)) _units.Remove(key);
        }

        public void Clear() {
            _units.Clear();
        }

        public string DefaultWorkingDirectory { get; set; }
    }
}
