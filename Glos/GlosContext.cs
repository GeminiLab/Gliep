using System.Collections.Generic;

namespace GeminiLab.Glos {
    public class GlosContext {
        public class GlosValueReferenceWrapper {
            private GlosValue _value;

            public GlosValueReferenceWrapper() => _value.SetNil();
            public GlosValueReferenceWrapper(in GlosValue value) => _value = value;

            public ref GlosValue GetReference() => ref _value;
        }

        private readonly Dictionary<string, GlosValueReferenceWrapper> _variables = new Dictionary<string, GlosValueReferenceWrapper>();
        private readonly Dictionary<string, GlosContext>               _location  = new Dictionary<string, GlosContext>();

        private GlosContext(GlosContext? parent, GlosContext? global) {
            Parent = parent;
            Global = global ?? this;
        }

        public GlosContext(GlosContext? parent)
            : this(parent, parent?.Global) { }

        public GlosContext? Parent { get; }
        public GlosContext Global { get; }

        public IReadOnlyDictionary<string, GlosValueReferenceWrapper> Variables => _variables;

        private GlosValueReferenceWrapper getWrapper(string name) => getWrapper(name, out _);

        private GlosValueReferenceWrapper getWrapper(string name, out GlosContext location) {
            if (_variables.TryGetValue(name, out var wrapper)) {
                location = this;
                return wrapper;
            }

            if (_location.TryGetValue(name, out location)) return location.getWrapper(name);

            if (Parent == null) {
                location = this;
                return _variables[name] = new GlosValueReferenceWrapper();
            } else {
                var rv = Parent.getWrapper(name, out location);
                _location[name] = location;
                return rv;
            }
        }

        public ref GlosValue GetVariableReference(string name) => ref getWrapper(name).GetReference();

        public void CreateVariable(string name) => _variables[name] = new GlosValueReferenceWrapper();
        public void CreateVariable(string name, in GlosValue value) => _variables[name] = new GlosValueReferenceWrapper(value);
    }
}
