﻿using System;
using System.Runtime.CompilerServices;

namespace GeminiLab.Glos {
    public enum GlosCoroutineStatus : byte {
        Invalid,
        Initial,
        InStack,
        Running,
        Suspend,
        Stopped,
    }

    public partial class GlosCoroutine {
        public GlosCoroutineStatus Status { get; private set; }

        public GlosViMa Parent { get; }

        public void ClearToResume() {
            if (Status != GlosCoroutineStatus.InStack) throw new InvalidOperationException();

            Status = GlosCoroutineStatus.Suspend;
        }

        public ExecResult Resume(ReadOnlySpan<GlosValue> args) {
            var argc = args.Length;
            args.CopyTo(_stack.AsSpan(_sptr, argc));

            switch (Status) {
            case GlosCoroutineStatus.Initial: {
                if (Entry.Type == GlosValueType.Function) {
                    pushNewStackFrame(0, Entry.AssumeFunction(), argc, RootContext, -1);
                } else if (Entry.Type == GlosValueType.AsyncEFunction) {
                    pushNewAsyncEFunctionStackFrame(0, Entry.AssumeAsyncEFunction().Call(this), -1);
                } else {
                    throw new ArgumentOutOfRangeException(nameof(Entry));
                }

                pushUntil(callStackTop().PrivateStackBase);

                goto case GlosCoroutineStatus.Suspend;
            }
            case GlosCoroutineStatus.Suspend: {
                Status = GlosCoroutineStatus.Running;

                var res = execute(0, true);

                Status = res.Result switch {
                    ExecResultType.Return => GlosCoroutineStatus.Stopped,
                    ExecResultType.Resume => GlosCoroutineStatus.InStack,
                    ExecResultType.Yield  => GlosCoroutineStatus.Suspend,
                    _                     => throw new NeverException()
                };

                return res;
            }
            case GlosCoroutineStatus.InStack:
            case GlosCoroutineStatus.Running:
            case GlosCoroutineStatus.Stopped: {
                throw new InvalidOperationException();
            }
            default:
                throw new NeverException();
            }
        }

        public static bool ReadInstructionAndImmediate(ReadOnlySpan<byte> code, ref int ip, out GlosOp op, out long imm, out bool immOnStack) {
            var len = code.Length;

            immOnStack = false;
            imm = 0;

            if (ip == len) {
                op = GlosOp.Ret;
                return true;
            }

            var opb = code[ip++];
            op = (GlosOp) opb;
            var immt = GlosOpInfo.Immediates[opb];

            unchecked {
                var imms = immt.ImmediateSize();

                if (imms == 0) {
                    if (immt == GlosOpImmediate.Embedded) {
                        imm = opb & 0x07;
                    } else if (immt == GlosOpImmediate.OnStack) {
                        immOnStack = true;
                    }
                } else {
                    if (ip + imms > len) {
                        return false;
                    }

                    unsafe {
                        fixed (byte* immp = &code[ip]) {
                            switch (immt) {
                            case GlosOpImmediate.Byte:
                                imm = (sbyte) *immp;
                                break;
                            case GlosOpImmediate.Word:
                                imm = *(short*) immp;
                                break;
                            case GlosOpImmediate.Dword:
                                imm = *(int*) immp;
                                break;
                            case GlosOpImmediate.Qword:
                                imm = *(long*) immp;
                                break;
                            }
                        }
                    }

                    ip += imms;
                }
            }

            return true;
        }

        private void pushNewStackFrame(int bptr, GlosFunction function, int argc, GlosContext? context, int returnSize) {
            ref var frame = ref pushCallStackFrame();

            frame.Function = function;
            frame.Call = null;
            frame.Context = context ?? new GlosContext(function.ParentContext);

            frame.StackBase = bptr;
            frame.ArgumentsCount = argc;
            frame.LocalVariablesBase = frame.StackBase + frame.ArgumentsCount;
            frame.PrivateStackBase = frame.LocalVariablesBase + function.Prototype.LocalVariableSize;
            frame.InstructionPointer = 0;
            frame.NextInstructionPointer = 0;
            frame.DelimiterStackBase = _dptr;
            frame.TryStackBase = _tptr;

            frame.ReturnSize = returnSize;

            foreach (var s in function.Prototype.VariableInContext) frame.Context.CreateVariable(s);
        }

        private void pushNewAsyncEFunctionStackFrame(int bptr, IGlosAsyncEFunctionCall call, int returnSize) {
            ref var frame = ref pushCallStackFrame();

            frame.Function = null;
            frame.Call = call;
            frame.Context = null!;

            frame.StackBase = bptr;
            frame.ArgumentsCount = 0;
            frame.LocalVariablesBase = bptr;
            frame.PrivateStackBase = bptr;
            frame.InstructionPointer = -1;
            frame.NextInstructionPointer = -1;
            frame.DelimiterStackBase = _dptr;
            frame.TryStackBase = _tptr;

            frame.ReturnSize = returnSize;
        }

        public enum ExecResultType {
            Return,
            Resume,
            Yield,
        }

        public ref struct ExecResult {
            public ExecResultType Result;
            public GlosValue[]    ReturnValues;
            public GlosCoroutine  CoroutineToResume;
        }

        private ExecResult execute(int callStackBase, bool allowCoroutineSchedule) {
            var bptr = callStackTop().StackBase;

            int ip = 0, nip = 0;
            byte phase = 0, nextPhase = 0, phaseCount = 0;
            GlosOp lastOp = GlosOp.Invalid;
            long lastImm = 0;
            GlosFunctionPrototype proto = null!;
            ReadOnlyMemory<byte> code = default;
            int len = 0;
            IGlosUnit unit = null!;
            GlosContext ctx = null!, global = null!;

            try {
                restoreStatus();

                while (_cptr > callStackBase) {
                    if (ip < 0 || ip > len || nip < 0 || nip > len) {
                        // we use ip: -1 and nip: -1 to mark a async external function
                        if (ip < 0 && nip < 0 && callStackTop().Call is { } call) {
                            var result = call.Resume(_stack.AsSpan(bptr));

                            popUntil(bptr);

                            switch (result.Type) {
                            case AsyncEFunctionResumeResultType.Return: {
                                var retc = result.Arguments.Length;
                                var returnSize = callStackTop().ReturnSize;
                                if (returnSize < 0) {
                                    returnSize = retc;
                                }

                                var copySize = returnSize > retc ? retc : returnSize;

                                result.Arguments.AsSpan(0, copySize).CopyTo(_stack.AsSpan(bptr, copySize));

                                popCallStackFrame();
                                restoreStatus();
                                break;
                            }
                            case AsyncEFunctionResumeResultType.Resume: {
                                var cor = result.Value.AssertCoroutine();
                                var args = result.Arguments;

                                return new ExecResult { Result = ExecResultType.Resume, CoroutineToResume = cor, ReturnValues = args };
                            }
                            case AsyncEFunctionResumeResultType.Yield: {
                                var args = result.Arguments;

                                return new ExecResult { Result = ExecResultType.Yield, ReturnValues = args };
                            }
                            case AsyncEFunctionResumeResultType.Call: {
                                var args = result.Arguments;
                                var argc = args.Length;
                                var funv = result.Value;

                                args.CopyTo(_stack.AsSpan(bptr, argc));
                                callFunction(funv, argc, -1);

                                break;
                            }
                            default:
                                throw new ArgumentOutOfRangeException();
                            }

                            continue;
                        }

                        throw new GlosInvalidInstructionPointerException();
                    }

                    GlosOp op;
                    long imms;

                    if (nextPhase == phaseCount) {
                        ip = nip;

                        if (!ReadInstructionAndImmediate(code.Span, ref nip, out op, out imms, out bool immOnStack)) {
                            throw new GlosUnexpectedEndOfCodeException();
                        }

                        if (immOnStack) {
                            imms = stackTop().AssertInteger();
                            popStack();
                        }

                        lastOp = op;
                        lastImm = imms;
                        phaseCount = GlosOpExecutionInfo.Phases[(int) op];
                        phase = 0;
                    } else {
                        op = lastOp;
                        imms = lastImm;
                        phase = nextPhase;
                    }

                    nextPhase = (byte) (phase + 1);

                    var cat = GlosOpInfo.Categories[(int) op];

                    // execution
                    switch (cat) {
                    case GlosOpCategory.ArithmeticOperator:
                        executeArithmeticOperation(op);
                        break;
                    case GlosOpCategory.ComparisonOperator:
                        executeComparisonOperation(op);
                        break;
                    case GlosOpCategory.TableVectorOperator when op == GlosOp.Smt:
                        stackTop().AssertTable().Metatable = stackTop(1).AssertTable();
                        popStack(2);
                        break;
                    case GlosOpCategory.TableVectorOperator when op == GlosOp.Gmt:
                        if (stackTop().AssertTable().Metatable is { } mt) {
                            stackTop().SetTable(mt);
                        } else {
                            stackTop().SetNil();
                        }

                        break;
                    case GlosOpCategory.TableVectorOperator when op == GlosOp.Pshv:
                        stackTop().AssertVector().Push(in stackTop(1));
                        popStack(2);
                        break;
                    case GlosOpCategory.TableVectorOperator when op == GlosOp.Popv: {
                        ref var tail = ref stackTop().AssertVector().PopRef();
                        stackTop() = tail;
                        tail.SetNil();
                        break;
                    }
                    case GlosOpCategory.TableVectorOperator:
                        executeTableVectorOperator(op);
                        break;
                    case GlosOpCategory.UnaryOperator:
                        executeUnaryOperation(op);
                        break;
                    case GlosOpCategory.ContextOperator when op == GlosOp.Rvc:
                        stackTop() = ctx.GetVariableReference(stackTop().AssertString());
                        break;
                    case GlosOpCategory.ContextOperator when op == GlosOp.Uvc:
                        ctx.GetVariableReference(stackTop().AssertString()) = stackTop(1);
                        popStack(2);
                        break;
                    case GlosOpCategory.ContextOperator when op == GlosOp.Rvg:
                        stackTop() = global.GetVariableReference(stackTop().AssertString());
                        break;
                    case GlosOpCategory.ContextOperator when op == GlosOp.Uvg:
                        global.GetVariableReference(stackTop().AssertString()) = stackTop(1);
                        popStack(2);
                        break;
                    case GlosOpCategory.LoadFunction when imms > unit.FunctionTable.Count || imms < 0:
                        throw new GlosFunctionIndexOutOfRangeException((int) imms);
                    case GlosOpCategory.LoadFunction:
                        pushStack().SetFunction(new GlosFunction(unit.FunctionTable[(int) imms]!, null!, unit));
                        break;
                    case GlosOpCategory.LoadString when imms > unit.StringTable.Count || imms < 0:
                        throw new GlosStringIndexOutOfRangeException((int) imms);
                    case GlosOpCategory.LoadString:
                        pushStack().SetString(unit.StringTable[(int) imms]);
                        break;
                    case GlosOpCategory.LoadInteger:
                        pushStack().SetInteger(op == GlosOp.LdNeg1 ? -1 : imms);
                        break;
                    case GlosOpCategory.LoadMisc when op == GlosOp.LdNTbl:
                        pushStack().SetTable(new GlosTable());
                        break;
                    case GlosOpCategory.LoadMisc when op == GlosOp.LdNil:
                        pushNil();
                        break;
                    case GlosOpCategory.LoadMisc when op == GlosOp.LdFlt:
                        pushStack().SetFloatByBinaryPresentation(unchecked((ulong) imms));
                        break;
                    case GlosOpCategory.LoadMisc when op == GlosOp.LdNVec:
                        pushStack().SetVector(new GlosVector());
                        break;
                    case GlosOpCategory.LoadMisc when op == GlosOp.LdTrue:
                        pushStack().SetBoolean(true);
                        break;
                    case GlosOpCategory.LoadMisc when op == GlosOp.LdFalse:
                        pushStack().SetBoolean(false);
                        break;
                    case GlosOpCategory.LoadMisc when op == GlosOp.LdArgc:
                        pushStack().SetInteger(callStackTop().ArgumentsCount);
                        break;
                    case GlosOpCategory.LoadLocalVariable when imms >= proto.LocalVariableSize || imms < 0:
                        throw new GlosLocalVariableIndexOutOfRangeException((int) imms);
                    case GlosOpCategory.LoadLocalVariable:
                        pushStack() = _stack[callStackTop().LocalVariablesBase + (int) imms]; // !!!!!
                        break;
                    case GlosOpCategory.StoreLocalVariable when imms >= proto.LocalVariableSize || imms < 0:
                        throw new GlosLocalVariableIndexOutOfRangeException((int) imms);
                    case GlosOpCategory.StoreLocalVariable:
                        _stack[callStackTop().LocalVariablesBase + (int) imms] = stackTop();
                        popStack();
                        break;
                    case GlosOpCategory.LoadArgument:
                        pushNil();
                        if (imms < callStackTop().ArgumentsCount && imms >= 0) stackTop() = _stack[callStackTop().StackBase + (int) imms];
                        break;
                    case GlosOpCategory.Branch:
                        var dest = nip + (int) imms;
                        var jump = op switch {
                            GlosOp.B    => true,
                            GlosOp.BS   => true,
                            GlosOp.Bf   => stackTop().Falsey(),
                            GlosOp.BfS  => stackTop().Falsey(),
                            GlosOp.Bt   => stackTop().Truthy(),
                            GlosOp.BtS  => stackTop().Truthy(),
                            GlosOp.Bn   => stackTop().IsNil(),
                            GlosOp.BnS  => stackTop().IsNil(),
                            GlosOp.Bnn  => stackTop().IsNonNil(),
                            GlosOp.BnnS => stackTop().IsNonNil(),
                            _           => false,
                        };

                        if (jump) {
                            nip = dest;
                        }

                        if (op != GlosOp.B && op != GlosOp.BS) popStack();
                        break;
                    case GlosOpCategory.ShpRv: {
                        var count = _sptr - popDelimiter();

                        while (count > imms && count > 0) {
                            popStack();
                            --count;
                        }

                        while (count < imms) {
                            pushNil();
                            ++count;
                        }

                        break;
                    }
                    case GlosOpCategory.Syscall:
                        Parent.GetSyscall((int) imms)?.Invoke(_stack, _callStack, _delStack);
                        break;
                    case GlosOpCategory.Others when op == GlosOp.Try || op == GlosOp.TryS:
                        pushTryStackFrame(nip + (int) imms);
                        break;
                    case GlosOpCategory.Others when op == GlosOp.EndTry:
                        popTryStackFrame();
                        break;
                    case GlosOpCategory.Others when op == GlosOp.Throw:
                        throw new NotImplementedException();
                        break;
                    case GlosOpCategory.Others when op == GlosOp.Dup:
                        pushStack();
                        stackTop() = stackTop(1);
                        break;
                    case GlosOpCategory.Others when op == GlosOp.Pop:
                        popStack();
                        break;
                    case GlosOpCategory.Others when op == GlosOp.Mkc:
                        var type = stackTop().Type;
                        if (type != GlosValueType.Function && type != GlosValueType.AsyncEFunction) {
                            throw new GlosValueTypeAssertionFailedException(stackTop(), GlosValueType.Function); // TODO
                        }

                        stackTop().SetCoroutine(new GlosCoroutine(Parent, stackTop()));
                        break;
                    case GlosOpCategory.Others when op == GlosOp.Yield: {
                        var yldb = popDelimiter();
                        var yldc = _sptr - yldb;

                        var yld = new GlosValue[yldc];
                        _stack.AsSpan(yldb, yldc).CopyTo(yld);
                        popUntil(yldb);

                        return new ExecResult { Result = ExecResultType.Yield, ReturnValues = yld };
                    }
                    case GlosOpCategory.Others when op == GlosOp.Resume: {
                        var cor = stackTop().AssumeCoroutine();
                        popStack();

                        var argb = peekDelimiter();
                        var argc = _sptr - argb;

                        var args = new GlosValue[argc];
                        _stack.AsSpan(argb, argc).CopyTo(args);
                        popUntil(argb);

                        return new ExecResult { Result = ExecResultType.Resume, CoroutineToResume = cor, ReturnValues = args };
                    }
                    case GlosOpCategory.Others when op == GlosOp.LdDel:
                        pushDelimiter();
                        break;
                    case GlosOpCategory.Others when op == GlosOp.Call: {
                        var funv = stackTop();
                        popStack();

                        var argb = peekDelimiter();
                        var argc = _sptr - argb;

                        callFunction(funv, argc, -1);
                        break;
                    }
                    case GlosOpCategory.Others when op == GlosOp.Ret: {
                        // clean up
                        var rtb = popDelimiter();
                        var retc = _sptr - rtb;

                        var returnSize = callStackTop().ReturnSize;
                        if (returnSize >= 0) {
                            while (retc > returnSize) {
                                popStack();
                                --retc;
                            }

                            while (retc < returnSize) {
                                pushNil();
                                ++retc;
                            }
                        }

                        _stack.AsSpan(rtb, retc).CopyTo(_stack.AsSpan(callStackTop().StackBase, retc));
                        popCurrentFrameTry();
                        popUntil(callStackTop().StackBase + retc);
                        popCurrentFrameDelimiter();
                        popCallStackFrame();
                        restoreStatus();
                        break;
                    }
                    case GlosOpCategory.Others when op == GlosOp.Bind:
                        stackTop().AssertFunction().ParentContext = ctx;
                        break;
                    case GlosOpCategory.Others when op == GlosOp.PopDel:
                        popDelimiter();
                        break;
                    case GlosOpCategory.Others when op == GlosOp.DupList: {
                        var del = peekDelimiter();
                        var count = _sptr - del;

                        pushDelimiter();

                        _stack.PreparePush(count);
                        _stack.AsSpan(del).CopyTo(_stack.AsSpan(_sptr, count));
                        break;
                    }
                    case GlosOpCategory.Others when op == GlosOp.Pkv: {
                        var del = popDelimiter();
                        var count = _sptr - del;

                        var vec = new GlosVector();

                        vec.Container().EnsureCapacity(count);
                        _stack.AsSpan(del, count).CopyTo(vec.Container().AsSpan(0, count));

                        while (_sptr > del) popStack();
                        pushStack(vec);
                        break;
                    }
                    case GlosOpCategory.Others when op == GlosOp.Upv: {
                        var vec = stackTop().AssertVector();
                        popStack();

                        pushDelimiter();
                        vec.Container().AsSpan().CopyTo(_stack.AsSpan(_stack.Count, vec.Container().Count));
                        break;
                    }
                    case GlosOpCategory.Others when op == GlosOp.Nop:
                        // nop;
                        break;
                    default:
                        throw new GlosUnknownOpException((byte) op);
                    }
                }

                var rc = _sptr - bptr;
                var rv = new GlosValue[rc];

                _stack.AsSpan(bptr, rc).CopyTo(rv);
                popUntil(bptr);

                return new ExecResult { Result = ExecResultType.Return, ReturnValues = rv };
            } catch (GlosException ex) when (!(ex is GlosRuntimeException)) {
                throw new GlosRuntimeException(this, ex);
            } finally {
                storeStatus();
            }

#region Local Functions

#if !DEVELOP && CS9
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            void callFunction(in GlosValue funv, int argc, int returnSize) {
                var bptr = _sptr - argc;

                funv.AssertInvokable();

                if (funv.Type == GlosValueType.EFunction || funv.Type == GlosValueType.PureEFunction) {
                    var args = new GlosValue[argc];
                    _stack.AsSpan(bptr, argc).CopyTo(args.AsSpan());

                    GlosValue[] rets;
                    if (funv.Type == GlosValueType.EFunction) {
                        rets = funv.AssertEFunction()(this, args);
                    } else {
                        rets = funv.AssertPureEFunction()(args);
                    }

                    var retc = rets.Length;
                    rets.AsSpan().CopyTo(_stack.AsSpan(bptr, retc));
                    popUntil(bptr + retc);
                } else if (funv.Type == GlosValueType.Function) {
                    storeStatus();
                    pushNewStackFrame(_sptr - argc, funv.AssertFunction(), argc, null, returnSize);
                    restoreStatus();
                    pushUntil(callStackTop().PrivateStackBase);
                } else if (funv.Type == GlosValueType.AsyncEFunction) {
                    storeStatus();
                    pushNewAsyncEFunctionStackFrame(_sptr - argc, funv.AssertAsyncEFunction().Call(this), returnSize);
                    restoreStatus();
                }
            }

#if !DEVELOP && CS9
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            void restoreStatus() {
                if (_cptr <= callStackBase) return;

                ref var frame = ref callStackTop();

                bptr = frame.StackBase;

                ip = frame.InstructionPointer;
                nip = frame.NextInstructionPointer;
                lastOp = frame.LastOp;
                phase = frame.Phase;
                nextPhase = frame.NextPhase;
                phaseCount = frame.PhaseCount;
                lastImm = frame.LastImm;
                proto = frame.Function?.Prototype;
                code = proto?.CodeMemory ?? ReadOnlyMemory<byte>.Empty;
                len = code.Length;
                unit = frame.Function?.Unit;
                ctx = frame.Context;
                global = ctx?.Global;
            }

#if !DEVELOP && CS9
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            void storeStatus() {
                if (_cptr <= callStackBase) return;

                ref var frame = ref callStackTop();

                frame.InstructionPointer = ip;
                frame.NextInstructionPointer = nip;
                frame.LastOp = lastOp;
                frame.Phase = phase;
                frame.NextPhase = nextPhase;
                frame.PhaseCount = phaseCount;
                frame.LastImm = lastImm;
            }

#if !DEVELOP && CS9
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            void executeArithmeticOperation(GlosOp op) {
                var useMetamethod = GlosValue.TryGetMetamethodOfOperand(in stackTop(1), in stackTop(), GlosMetamethodNames.FromOp(op), false, out var mm);

                if (!useMetamethod) {
                    GlosValueStaticCalculator.ExecuteBinaryOperation(ref stackTop(1), in stackTop(1), in stackTop(), op);
                    popStack();
                } else {
                    callFunction(mm, 2, 1);
                }
            }

#if !DEVELOP && CS9
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            void executeComparisonOperation(GlosOp op) {
                if (op == GlosOp.Lss || op == GlosOp.Gtr) {
                    if (phase == 0) {
                        var useMetamethod = GlosValue.TryGetMetamethodOfOperand(in stackTop(1), in stackTop(), GlosMetamethodNames.Lss, false, out var lss);

                        if (!useMetamethod) {
                            GlosValueStaticCalculator.ExecuteBinaryOperation(ref stackTop(1), in stackTop(1), in stackTop(), op);
                            popStack();
                            nextPhase = phaseCount;
                        } else {
                            if (op == GlosOp.Gtr) {
                                GlosValue.Swap(ref stackTop(1), ref stackTop());
                            }

                            callFunction(lss, 2, 1);
                        }
                    } else { // last phase
                        stackTop().SetBoolean(stackTop().Truthy());
                    }
                } else if (op == GlosOp.Equ || op == GlosOp.Neq) {
                    if (phase == 0) {
                        var useMetamethod = GlosValue.TryGetMetamethodOfOperand(in stackTop(1), in stackTop(), GlosMetamethodNames.Equ, false, out var equ);

                        if (!useMetamethod) {
                            GlosValueStaticCalculator.ExecuteBinaryOperation(ref stackTop(1), in stackTop(1), in stackTop(), op);
                            popStack();
                            nextPhase = phaseCount;
                        } else {
                            callFunction(equ, 2, 1);
                        }
                    } else { // last phase
                        if (op == GlosOp.Neq) {
                            stackTop().SetBoolean(stackTop().Falsey());
                        } else {
                            stackTop().SetBoolean(stackTop().Truthy());
                        }
                    }
                } else if (op == GlosOp.Leq || op == GlosOp.Geq) {
                    // if we should use metamethods, the stack will be like:
                    // 
                    // - after the first phase:
                    // 
                    // | opl | opr | equ metamethod | args for lss metamethod
                    // ---------------------------------------------------------
                    // |  by user, |            by vima              
                    // |  swapped  |
                    // |  if geq   |
                    // 
                    // - before the second phase:
                    // 
                    // | opl | opr | equ metamethod | result from lss metamethod
                    // ---------------------------------------------------------
                    // |  by user, |            by vima              
                    // |  swapped  |
                    // |  if geq   |
                    // 
                    // - if lss gives a affirmative answer, we skip the last phase, otherwise the stack will be (after the second phase):
                    //
                    // | opl | opr | args for equ metamethod
                    // ---------------------------------------------------------
                    // |  by user, |            by vima              
                    // |  swapped  |
                    // |  if geq   |
                    //
                    // - and before the last phase:
                    // 
                    // | opl | opr | final answer
                    // ---------------------------------------------------------
                    // |  by user, |            by vima              
                    // |  swapped  |
                    // |  if geq   |
                    if (phase == 0) {
                        var useMetamethod = GlosValue.TryGetMetamethodOfOperand(in stackTop(1), in stackTop(), GlosMetamethodNames.Lss, false, out var lss);
                        useMetamethod = GlosValue.TryGetMetamethodOfOperand(in stackTop(1), in stackTop(), GlosMetamethodNames.Equ, false, out var equ) && useMetamethod;

                        if (!useMetamethod) {
                            GlosValueStaticCalculator.ExecuteBinaryOperation(ref stackTop(1), in stackTop(1), in stackTop(), op);
                            popStack();
                            stackTop().SetBoolean(stackTop().Truthy());

                            nextPhase = phaseCount;
                        } else {
                            if (op == GlosOp.Geq) {
                                GlosValue.Swap(ref stackTop(1), ref stackTop());
                            }

                            pushStack(equ);

                            pushStack(in stackTop(2));
                            pushStack(in stackTop(2));

                            callFunction(lss, 2, 1);
                        }
                    } else if (phase == 1) {
                        stackTop().SetBoolean(stackTop().Truthy());
                        if (stackTop().AssumeBoolean()) {
                            popStack(3);
                            stackTop().SetBoolean(true);

                            nextPhase = phaseCount;
                        } else {
                            var equ = stackTop(1);
                            popStack(2);

                            pushStack(in stackTop(1));
                            pushStack(in stackTop(1));

                            callFunction(equ, 2, 1);
                        }
                    } else { // last phase
                        stackTop(2).SetBoolean(stackTop().Truthy());
                        popStack(2);
                    }
                }
            }

#if !DEVELOP && CS9
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            void executeTableVectorOperator(GlosOp op) {
                if (phase == 0) { // first phase: get hash
                    if (op == GlosOp.Ren) {
                        if (GlosValue.TryGetMetamethodOfOperand(in stackTop(1), GlosMetamethodNames.Ren, out var ren)) {
                            nextPhase = phaseCount;
                            callFunction(ren, 2, 1);

                            return;
                        }
                    }

                    if (op == GlosOp.Uen) {
                        if (GlosValue.TryGetMetamethodOfOperand(in stackTop(1), GlosMetamethodNames.Uen, out var ren)) {
                            var value = stackTop(2);
                            stackTop(2) = stackTop(1);
                            stackTop(1) = stackTop();
                            stackTop() = value;

                            nextPhase = phaseCount;
                            callFunction(ren, 3, 1);

                            return;
                        }
                    }

                    ref GlosValue key = ref (op == GlosOp.Ien ? ref stackTop(1) : ref stackTop());

                    pushStack(-1);
                    if (GlosValue.TryGetMetamethodOfOperand(in key, GlosMetamethodNames.Hash, out var fun)) {
                        pushStack(in key);

                        callFunction(fun, 1, 1);
                    } else {
                        pushStack(unchecked((long) key.Hash()));
                    }
                } else if (phase == 1) { // second phase: find entry
                    var eid = unchecked((int) stackTop(1).AssertInteger());
                    var hash = unchecked((ulong) stackTop().AssertInteger());

                    ref var key = ref (op == GlosOp.Ien ? ref stackTop(3) : ref stackTop(2));
                    var table = (op == GlosOp.Ien ? ref stackTop(4) : ref stackTop(3)).AssertTable();

                    eid = table.NextEntry(hash, eid);

                    stackTop(1).SetInteger(eid);

                    if (eid >= 0) {
                        ref var entry = ref table.GetEntryAt(eid);

                        if (GlosValue.TryGetMetamethodOfOperand(in key, in entry.Key, GlosMetamethodNames.Equ, false, out var equ)) {
                            pushStack(in key);
                            pushStack(in entry.Key);

                            callFunction(equ, 2, 1);
                        } else {
                            pushStack(GlosValueStaticCalculator.Equals(in key, in entry.Key));
                        }
                    } else {
                        nextPhase = 3; // jump to last phase
                    }
                } else if (phase == 2) { // third phase: check equality
                    var equals = stackTop().Truthy();
                    popStack();

                    if (!equals) {
                        nextPhase = 1; // jump to phase 1
                    }
                } else if (phase == 3) {
                    var eid = unchecked((int) stackTop(1).AssertInteger());
                    var hash = unchecked((ulong) stackTop().AssertInteger());

                    switch (op) {
                    case GlosOp.Ren:
                    case GlosOp.RenL: {
                        var table = stackTop(3).AssertTable();
                        ref var key = ref stackTop(2);

                        if (eid < 0) {
                            stackTop(3).SetNil();
                        } else {
                            stackTop(3) = table.GetEntryAt(eid).Value;
                        }

                        popStack(3);
                        break;
                    }
                    case GlosOp.Uen:
                    case GlosOp.UenL: {
                        var table = stackTop(3).AssertTable();
                        ref var key = ref stackTop(2);
                        ref var value = ref stackTop(4);

                        if (eid < 0) {
                            table.NewEntry(hash, in key, in value);
                        } else {
                            table.GetEntryAt(eid).Value = value;
                        }

                        popStack(5);
                        break;
                    }
                    case GlosOp.Ien: {
                        var table = stackTop(4).AssertTable();
                        ref var key = ref stackTop(3);
                        ref var value = ref stackTop(2);

                        if (eid < 0) {
                            table.NewEntry(hash, in key, in value);
                        } else {
                            table.GetEntryAt(eid).Value = value;
                        }

                        popStack(4);
                        break;
                    }
                    }
                }
            }

#if !DEVELOP && CS9
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            void executeUnaryOperation(GlosOp op) {
                var useMetamethod = GlosValue.TryGetMetamethodOfOperand(in stackTop(), GlosMetamethodNames.FromOp(op), out var mm);

                if (!useMetamethod) {
                    GlosValueStaticCalculator.ExecuteUnaryOperation(ref stackTop(), in stackTop(), op);
                } else {
                    callFunction(mm, 1, 1);
                }
            }

#endregion
        }

        public GlosValue[] ExecuteFunctionSync(GlosFunction function, GlosValue[]? args = null, GlosContext? thisContext = null) {
            var bptr = _sptr;
            var callStackBase = _cptr;
            var firstArgc = args?.Length ?? 0;
            args?.AsSpan().CopyTo(_stack.AsSpan(bptr, firstArgc));

            pushNewStackFrame(bptr, function, firstArgc, thisContext, -1);
            pushUntil(callStackTop().PrivateStackBase);

            var result = execute(callStackBase, false);

            if (result.Result != ExecResultType.Return) {
                throw new NeverException();
            }

            return result.ReturnValues;
        }

        public  GlosValue    Entry       { get; }
        private GlosContext? RootContext { get; }

        public GlosCoroutine(GlosViMa parent, GlosValue entry) : this(parent, entry, null) { }

        public GlosCoroutine(GlosViMa parent, GlosValue entry, GlosContext? rootContext) {
            Status = GlosCoroutineStatus.Invalid;
            Parent = parent;
            Entry = entry;
            RootContext = rootContext;

            Status = GlosCoroutineStatus.Initial;
        }
    }
}
