﻿using System;
using System.Collections.Generic;

namespace GeminiLab.Glos.ViMa {
    public class UnexpectedEndOfOpsException : Exception { }

    public class LocalVariableIndexOutOfRangeException : Exception { }

    public class InvalidProgramCounterException : Exception { }

    public class InvalidOperandTypeException : Exception { }

    public class StackUnderflowException : Exception { }

    public partial class GlosViMa {
        #region unit
        // Answer: ViMa should not manage units.
        #endregion

        #region stack
        // TODO: make eval stack flexible and remove this constant
        internal const int MaxStack = 0x10000;

        private readonly GlosValue[] _stack = new GlosValue[MaxStack];
        private long _sptr = 0;

        private ref GlosValue stackTop() {
            return ref _stack[_sptr - 1];
        }

        private ref GlosValue stackTop(int count) {
            return ref _stack[_sptr - count - 1];
        }

        private void copy(out GlosValue dest, in GlosValue src) {
            dest = src;
        }

        // TODO: add stack underflow check
        private void popStack() {
            --_sptr;
            _stack[_sptr].SetNil();
        }

        private void popStack(int count) {
            while (count-- > 0) popStack();
        }

        private void pushStack(in GlosValue value) {
            _stack[_sptr] = value;
            ++_sptr;
        }

        private ref GlosValue pushStack() {
            return ref _stack[_sptr++];
        }

        private ref GlosValue pushNil() => ref pushStack();
        #endregion

        #region global
        public GlosTable? GlobalEnvironment { get; set; }
        #endregion

        public GlosValue.Comparer Comparer => new GlosValue.Comparer(this);
    }
}
