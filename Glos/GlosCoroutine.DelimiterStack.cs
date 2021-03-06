using System;

namespace GeminiLab.Glos {
    public partial class GlosCoroutine {
        private readonly GlosStack<int> _delStack = new GlosStack<int>();

        private int _dptr => _delStack.Count;

        private bool hasDelimiter() => _dptr > callStackTop().DelimiterStackBase;

        private int peekDelimiter() => hasDelimiter() ? _delStack.StackTop() : callStackTop().PrivateStackBase;

        private int popDelimiter() {
            if (_dptr - 1 < _dlmt) {
                throw new InvalidOperationException();
            }
            
            return hasDelimiter() ? _delStack.PopStack() : callStackTop().PrivateStackBase;
        }

        private void popCurrentFrameDelimiter() {
            while (hasDelimiter()) popDelimiter();
        }

        private void pushDelimiter() => _delStack.PushStack() = _sptr;

        private void pushDelimiter(int pos) => _delStack.PushStack() = pos;
    }
}
