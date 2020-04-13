using System;

namespace GeminiLab.Glos {
    public partial class GlosViMa {
        public static bool ReadInstructionAndImmediate(ReadOnlySpan<byte> code, ref int ip, out GlosOp op, out long imm, out bool immOnStack) {
            var len = code.Length;

            immOnStack = false;
            imm = 0;

            if (ip == len) {
                op = GlosOp.Ret;
                return true;
            }

            var opb = code[ip++];
            op = (GlosOp)opb;
            var immt = GlosOpInfo.Immediates[opb];

            unchecked {
                switch (immt) {
                    case GlosOpImmediate.Embedded:
                        imm = opb & 0x07;
                        break;
                    case GlosOpImmediate.Byte:
                        if (ip + 1 > len) return false;
                        imm = (sbyte)code[ip++];
                        break;
                    case GlosOpImmediate.Dword:
                        if (ip + 4 > len) return false;
                        imm = unchecked((int)(uint)((ulong)code[ip] | ((ulong)code[ip + 1] << 8) | ((ulong)code[ip + 2] << 16) | ((ulong)code[ip + 3] << 24)));
                        ip += 4;
                        break;
                    case GlosOpImmediate.Qword:
                        if (ip + 8 > len) return false;
                        imm = unchecked((long)((ulong)code[ip] | ((ulong)code[ip + 1] << 8) | ((ulong)code[ip + 2] << 16) | ((ulong)code[ip + 3] << 24) | ((ulong)code[ip + 4] << 32) | ((ulong)code[ip + 5] << 40) | ((ulong)code[ip + 6] << 48) | ((ulong)code[ip + 7] << 56)));
                        ip += 8;
                        break;
                    case GlosOpImmediate.OnStack:
                        immOnStack = true;
                        break;
                }
            }

            return true;
        }

        private void pushNewCallFrame(int bptr, GlosFunction function, int argc) {
            ref var frame = ref pushCallStackFrame();

            frame.Function = function;
            frame.Context = new GlosContext(function.ParentContext);

            frame.StackBase = bptr;
            frame.ArgumentsBase = bptr;
            frame.ArgumentsCount = argc;
            frame.LocalVariablesBase = frame.ArgumentsBase + frame.ArgumentsCount;
            frame.PrivateStackBase = frame.LocalVariablesBase + function.Prototype.LocalVariableSize;
            frame.InstructionPointer = 0;
            frame.DelimiterStackBase = _dptr;

            foreach (var s in function.Prototype.VariableInContext) frame.Context.CreateVariable(s);
        }

        // TODO: assess whether this struct is necessary
        // is it really slower fetching following values from call stack than caching them here?
        private ref struct ExecutorContext {
            public int CallStackBase;
            public int CurrentCallStack;
            public int InstructionPointer;
            public GlosFunctionPrototype Prototype;
            public ReadOnlySpan<byte> Code;
            public int Length;
            public GlosUnit Unit;
            public GlosContext Context;
            public GlosContext Global;
        }

        void restoreStatus(ref ExecutorContext ctx) {
            if (_cptr <= ctx.CallStackBase) return;
            ref var frame = ref callStackTop();

            ctx.CurrentCallStack = _cptr - 1;
            ctx.InstructionPointer = frame.InstructionPointer;
            ctx.Prototype = frame.Function.Prototype;
            ctx.Code = ctx.Prototype.Code;
            ctx.Length = ctx.Code.Length;
            ctx.Unit = ctx.Prototype.Unit;
            ctx.Context = frame.Context;
            ctx.Global = ctx.Context.Root;
        }

        void storeStatus(ref ExecutorContext ctx) {
            ref var frame = ref _callStack[ctx.CurrentCallStack];

            frame.InstructionPointer = ctx.InstructionPointer;
        }

        public GlosValue[] ExecuteFunction(GlosFunction function, GlosValue[]? args = null) {
            var callStackBase = _cptr;
            var bptr = _sptr;

            ExecutorContext execCtx = default;
            execCtx.CallStackBase = callStackBase;

            ref var ip = ref execCtx.InstructionPointer;
            ref GlosFunctionPrototype proto = ref execCtx.Prototype!;
            ref var code = ref execCtx.Code;
            ref var len = ref execCtx.Length;
            ref GlosUnit unit = ref execCtx.Unit!;
            ref GlosContext ctx = ref execCtx.Context!;
            ref GlosContext global = ref execCtx.Global!;

            try {
                var firstArgc = args?.Length ?? 0;
                args?.AsSpan().CopyTo(_stack.AsSpan(bptr, firstArgc));

                pushNewCallFrame(bptr, function, firstArgc);
                restoreStatus(ref execCtx);
                pushUntil(callStackTop().PrivateStackBase);

                while (_cptr > callStackBase) {
                    if (ip < 0 || ip > len) throw new GlosInvalidInstructionPointerException();

                    if (!ReadInstructionAndImmediate(code, ref ip, out var op, out long imms, out bool immOnStack))
                        throw new GlosUnexpectedEndOfCodeException();

                    if (immOnStack) {
                        imms = stackTop().AssertInteger();
                        popStack();
                    }

                    // the implicit return at the end of function
                    var cat = GlosOpInfo.Categories[(int)op];

                    // execution
                    if (cat == GlosOpCategory.BinaryOperator) {
                        Calculator.ExecuteBinaryOperation(ref stackTop(1), in stackTop(1), in stackTop(), op);
                        popStack();
                    } else if (op == GlosOp.Smt) {
                        stackTop().AssertTable().Metatable = stackTop(1).AssertTable();
                        popStack(2);
                    } else if (op == GlosOp.Gmt) {
                        if (stackTop().AssertTable().Metatable is {} mt) {
                            stackTop().SetTable(mt);
                        } else {
                            stackTop().SetNil();
                        }
                    } else if (op == GlosOp.Ren) {
                        if (stackTop(1).AssertTable().TryReadEntry(stackTop(), out var result)) {
                            stackTop(1) = result;
                        } else {
                            stackTop(1).SetNil();
                        }

                        popStack();
                    } else if (op == GlosOp.Uen) {
                        stackTop(1).AssertTable().UpdateEntry(stackTop(), stackTop(2));

                        popStack(3);
                    } else if (op == GlosOp.RenL) {
                        if (stackTop(1).AssertTable().TryReadEntryLocally(stackTop(), out var result)) {
                            stackTop(1) = result;
                        } else {
                            stackTop(1).SetNil();
                        }

                        popStack();
                    } else if (op == GlosOp.UenL) {
                        stackTop(1).AssertTable().UpdateEntryLocally(stackTop(), stackTop(2));

                        popStack(3);
                    } else if (op == GlosOp.Ien) {
                        stackTop(2).AssertTable().UpdateEntryLocally(stackTop(1), stackTop());

                        popStack(2);
                    } else if (cat == GlosOpCategory.UnaryOperator) {
                        Calculator.ExecuteUnaryOperation(ref stackTop(), in stackTop(), op);
                    } else if (op == GlosOp.Rvc) {
                        stackTop() = ctx.GetVariableReference(stackTop().AssertString());
                    } else if (op == GlosOp.Uvc) {
                        ctx.GetVariableReference(stackTop().AssertString()) = stackTop(1);
                        popStack(2);
                    } else if (op == GlosOp.Rvg) {
                        stackTop() = global.GetVariableReference(stackTop().AssertString());
                    } else if (op == GlosOp.Uvg) {
                        global.GetVariableReference(stackTop().AssertString()) = stackTop(1);
                        popStack(2);
                    } else if (op == GlosOp.LdFun || op == GlosOp.LdFunS) {
                        if (imms > unit.FunctionTable.Count || imms < 0) throw new ArgumentOutOfRangeException();
                        int index = (int)imms;
                        pushStack().SetFunction(new GlosFunction(unit.FunctionTable[index]!, null!));
                    } else if (op == GlosOp.LdStr || op == GlosOp.LdStrS) {
                        if (imms > unit.StringTable.Count || imms < 0) throw new ArgumentOutOfRangeException();
                        int index = (int)imms;
                        pushStack().SetString(unit.StringTable[index]);
                    } else if (cat == GlosOpCategory.LoadInteger) {
                        pushStack().SetInteger(op == GlosOp.LdNeg1 ? -1 : imms);
                    } else if (op == GlosOp.LdNTbl) {
                        pushStack().SetTable(new GlosTable(this));
                    } else if (op == GlosOp.LdNil) {
                        pushNil();
                    } else if (op == GlosOp.LdFlt) {
                        pushStack().SetFloatByBinaryPresentation(unchecked((ulong)imms));
                    } else if (op == GlosOp.LdTrue) {
                        pushStack().SetBoolean(true);
                    } else if (op == GlosOp.LdFalse) {
                        pushStack().SetBoolean(false);
                    } else if (cat == GlosOpCategory.LoadLocalVariable) {
                        if (imms >= proto.LocalVariableSize || imms < 0)
                            throw new GlosLocalVariableIndexOutOfRangeException((int)imms);
                        pushStack() = _stack[callStackTop().LocalVariablesBase + (int)imms]; // !!!!!
                    } else if (cat == GlosOpCategory.StoreLocalVariable) {
                        if (imms >= proto.LocalVariableSize || imms < 0)
                            throw new GlosLocalVariableIndexOutOfRangeException((int)imms);
                        _stack[callStackTop().LocalVariablesBase + (int)imms] = stackTop();
                        popStack();
                    } else if (cat == GlosOpCategory.LoadArgument) {
                        pushNil();
                        if (imms < callStackTop().ArgumentsCount && imms >= 0)
                            stackTop() = _stack[callStackTop().ArgumentsBase + (int)imms];
                    } else if (op == GlosOp.LdArgc) {
                        pushStack().SetInteger(callStackTop().ArgumentsCount);
                    } else if (cat == GlosOpCategory.Branch) {
                        var dest = ip + (int)imms;
                        var jump = op switch {
                            GlosOp.B => true,
                            GlosOp.BS => true,
                            GlosOp.Bf => stackTop().Falsey(),
                            GlosOp.BfS => stackTop().Falsey(),
                            GlosOp.Bt => stackTop().Truthy(),
                            GlosOp.BtS => stackTop().Truthy(),
                            GlosOp.Bn => stackTop().IsNil(),
                            GlosOp.BnS => stackTop().IsNil(),
                            GlosOp.Bnn => stackTop().IsNonNil(),
                            GlosOp.BnnS => stackTop().IsNonNil(),
                            _ => false,
                        };

                        if (jump) ip = dest;

                        if (op != GlosOp.B && op != GlosOp.BS) popStack();
                    } else if (op == GlosOp.Dup) {
                        pushStack();
                        stackTop() = stackTop(1);
                    } else if (op == GlosOp.Pop) {
                        popStack();
                    } else if (op == GlosOp.LdDel) {
                        pushDelimiter();
                    } else if (op == GlosOp.Call) {
                        var funv = stackTop();
                        popStack();

                        var ptr = peekDelimiter();
                        var nextArgc = _sptr - ptr;

                        if (funv.Type == GlosValueType.ExternalFunction) {
                            var fun = funv.AssertExternalFunction();

                            var nextArgs = new GlosValue[nextArgc];
                            _stack.AsSpan(ptr, nextArgc).CopyTo(nextArgs.AsSpan());
                            var nextRets = fun(nextArgs);
                            var nextRetc = nextRets.Length;
                            nextRets.AsSpan().CopyTo(_stack.AsSpan(ptr, nextRetc));
                            popUntil(ptr + nextRetc);
                        } else if (funv.Type == GlosValueType.Function) {
                            var fun = funv.AssertFunction();
                            storeStatus(ref execCtx);
                            pushNewCallFrame(ptr, fun, nextArgc);
                            restoreStatus(ref execCtx);
                            pushUntil(callStackTop().PrivateStackBase);
                        } else {
                            throw new GlosValueNotCallableException(funv);
                        }
                    } else if (op == GlosOp.Ret) {
                        // clean up
                        var rtb = popDelimiter();
                        var retc = _sptr - rtb;

                        _stack.AsSpan(rtb, retc).CopyTo(_stack.AsSpan(callStackTop().StackBase, retc));
                        popUntil(callStackTop().StackBase + retc);
                        popCurrentFrameDelimiter();
                        popCallStackFrame();
                        restoreStatus(ref execCtx);
                    } else if (op == GlosOp.Bind) {
                        stackTop().AssertFunction().ParentContext = ctx;
                    } else if (cat == GlosOpCategory.ShpRv) {
                        var count = _sptr - popDelimiter();

                        while (count > imms && count > 0) {
                            popStack();
                            --count;
                        }

                        while (count < imms) {
                            pushNil();
                            ++count;
                        }
                    } else if (op == GlosOp.PopDel) {
                        popDelimiter();
                    } else if (op == GlosOp.DupList) {
                        var del = peekDelimiter();
                        var count = _sptr - del;

                        pushDelimiter();

                        _stack.PreparePush(count);
                        _stack.AsSpan(del).CopyTo(_stack.AsSpan(_sptr, count));
                    } else if (op == GlosOp.Nop) {
                        // nop;
                    } else if (cat == GlosOpCategory.Syscall) {
                        _syscalls[(int)imms]?.Invoke(_stack, _callStack, _delStack);
                    } else {
                        throw new GlosUnknownOpException((byte)op);
                    }
                }

                var rc = _sptr - bptr;
                var rv = new GlosValue[rc];

                _stack.AsSpan(bptr, rc).CopyTo(rv);
                popUntil(bptr);
                return rv;
            } catch (GlosException ex) when (!(ex is GlosRuntimeException)) {
                throw new GlosRuntimeException(this, ex);
            } finally {
                storeStatus(ref execCtx);
            }
        }

        // though ViMa shouldn't manage units, this function is necessary
        public GlosValue[] ExecuteUnit(GlosUnit unit, GlosValue[]? args = null, GlosContext? parentContext = null) {
            return ExecuteFunction(new GlosFunction(unit.FunctionTable[unit.Entry], parentContext ?? new GlosContext(null)), args);
        }
    }
}