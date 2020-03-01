﻿using GeminiLab.Core2.Text;
using GeminiLab.Glos;
using GeminiLab.Glos.ViMa;

using Xunit;
using XUnitTester.Checker;

namespace XUnitTester.Glug {
    public class GlugTest : GlugExecutionTestBase {
        [Fact]
        public void Return0() {
            GlosValueArrayChecker.Create(Execute("0"))
                .First().AssertInteger(0)
                .MoveNext().AssertEnd();
        }

        [Fact]
        public void Evaluation() {
            var code = @"
                [1, 2, false, (), nil, -(1 + 2), if (true) 1, if (false) 2, `{}]
            ";

            GlosValueArrayChecker.Create(Execute(code))
                .First().AssertInteger(1)
                .MoveNext().AssertInteger(2)
                .MoveNext().AssertFalse()
                .MoveNext().AssertNil()
                .MoveNext().AssertNil()
                .MoveNext().AssertInteger(-3)
                .MoveNext().AssertInteger(1)
                .MoveNext().AssertNil()
                .MoveNext().AssertNil()
                .MoveNext().AssertEnd();
        }

        [Fact]
        public void Fibonacci() {
            var code = @"
                fn fibo [x] if (x <= 1) x else fibo[x - 2] + fibo[x - 1];
                return [fibo[0], fibo[1], fibo[10]];
            ";

            GlosValueArrayChecker.Create(Execute(code))
                .First().AssertInteger(0)
                .MoveNext().AssertInteger(1)
                .MoveNext().AssertInteger(55)
                .MoveNext().AssertEnd();
        }

        [Fact]
        public void Counter() {
            var code = @"
                fn counter [begin] ( begin = begin - 1; fn -> begin = begin + 1 );
                [!ca, cb, !cc, cd] = [counter 0, counter(0), counter[7], counter$-1];

                return [ca[], ca[], ca[], cb[], ca[], cc[], ca[], cd[], ca[]];
            ";

            GlosValueArrayChecker.Create(Execute(code))
                .First().AssertInteger(0)
                .MoveNext().AssertInteger(1)
                .MoveNext().AssertInteger(2)
                .MoveNext().AssertInteger(0)
                .MoveNext().AssertInteger(3)
                .MoveNext().AssertInteger(7)
                .MoveNext().AssertInteger(4)
                .MoveNext().AssertInteger(-1)
                .MoveNext().AssertInteger(5)
                .MoveNext().AssertEnd();
        }

        [Fact]
        public void RecursiveGcd() {
            var code = @"
                !gcd = [a, b] -> if (a > b) gcd[b, a] elif (~(0 < a)) b else gcd[b % a, a];
                [gcd[4, 6], gcd[2, 1], gcd[117, 39], gcd[1, 1], gcd[15, 28]]
            ";

            GlosValueArrayChecker.Create(Execute(code))
                .First().AssertInteger(2)
                .MoveNext().AssertInteger(1)
                .MoveNext().AssertInteger(39)
                .MoveNext().AssertInteger(1)
                .MoveNext().AssertInteger(1)
                .MoveNext().AssertEnd();
        }

        [Fact]
        public void YCombinator() {
            var code = @"
                Y = f -> (x -> f(x x))(x -> f(v -> x x v));
                Y (f -> n -> if (n == 0) 1 else n * f(n - 1)) 10
            ";

            GlosValueArrayChecker.Create(Execute(code))
                .First().AssertInteger(3628800)
                .MoveNext().AssertEnd();
        }

        [Fact]
        public void String() {
            string strA = "strA", strB = "ユニコードイグザンプル\u4396";
            string strEscape = "\\n";
            var code = $@"
                [""{strA}"", ""{strA}"" + ""{strB}"", ""{strEscape}""]
            ";

            GlosValueArrayChecker.Create(Execute(code))
                .First().AssertString(strA)
                .MoveNext().AssertString(strA + strB)
                .MoveNext().AssertString(EscapeSequenceConverter.Decode(strEscape))
                .MoveNext().AssertEnd();
        }

        [Fact]
        public void Beide() {
            var code = @"
                fn beide -> [1, 2];
                fn sum[x, y] x + y;

                return [beide[], beide[] - 1, sum$beide[]]
            ";

            GlosValueArrayChecker.Create(Execute(code))
                .First().AssertInteger(1)
                .MoveNext().AssertInteger(0)
                .MoveNext().AssertInteger(3)
                .MoveNext().AssertEnd();
        }

        [Fact]
        public void DeepRecursiveLoop() {
            var code = @"
                fn loop[from, to, step, body] (
                    if (from < to) (
                        body[from];
                        loop[from + step, to, step, body];
                    )
                );
                !sum = 0;
                loop[1, 131072 + 1, 1, i -> sum = sum + i];
                !mul = 1;
                loop[1, 10, 1, i -> mul = mul * i];
                return [sum, mul];
            ";

            GlosValueArrayChecker.Create(Execute(code))
                .First().AssertInteger((1L + 131072) * 131072 / 2)
                .MoveNext().AssertInteger(362880)
                .MoveNext().AssertEnd();
        }

        [Fact]
        public void GlobalAndLocal() {
            var code = @"
                fn a -> x = 1;
                fn b -> !!x = 2;
                fn c -> !!x;

                return [c[], a[], b[], a[], c[], !!ext[], c[]]
            ";

            var context = new GlosContext(null);
            context.CreateVariable("ext", (GlosExternalFunction)((param) => { context.GetVariableReference("x") = 3; return new GlosValue[] { 3 }; }));

            GlosValueArrayChecker.Create(Execute(code, context))
                .First().AssertNil()
                .MoveNext().AssertInteger(1)
                .MoveNext().AssertInteger(2)
                .MoveNext().AssertInteger(1)
                .MoveNext().AssertInteger(2)
                .MoveNext().AssertInteger(3)
                .MoveNext().AssertInteger(3)
                .MoveNext().AssertEnd();
        }

        [Fact]
        public void Table() {
            var code = @"
                d = ""d"";
                e = ""ee"";
                a = { .a: 1, .b: 2, d: 4, @e: 5 };
                a.c = 3;
                [a.a, a.b, a.c, a@d, a.ee]
            ";

            GlosValueArrayChecker.Create(Execute(code))
                .First().AssertInteger(1)
                .MoveNext().AssertInteger(2)
                .MoveNext().AssertInteger(3)
                .MoveNext().AssertInteger(4)
                .MoveNext().AssertInteger(5)
                .MoveNext().AssertEnd();
        }

        [Theory]
        [InlineData("1 + true", GlosOp.Add, GlosValueType.Integer, GlosValueType.Boolean)]
        [InlineData("\"\" - 1", GlosOp.Sub, GlosValueType.String, GlosValueType.Integer)]
        [InlineData("nil * {}", GlosOp.Mul, GlosValueType.Nil, GlosValueType.Table)]
        [InlineData("2 / \"\"", GlosOp.Div, GlosValueType.Integer, GlosValueType.String)]
        [InlineData("\"\" % 2", GlosOp.Mod, GlosValueType.String, GlosValueType.Integer)]
        [InlineData("true << false", GlosOp.Lsh, GlosValueType.Boolean, GlosValueType.Boolean)]
        [InlineData("\"\" >> 44353", GlosOp.Rsh, GlosValueType.String, GlosValueType.Integer)]
        [InlineData("true & 1", GlosOp.And, GlosValueType.Boolean, GlosValueType.Integer)]
        [InlineData("1 | \"\"", GlosOp.Orr, GlosValueType.Integer, GlosValueType.String)]
        [InlineData("{} ^ nil", GlosOp.Xor, GlosValueType.Table, GlosValueType.Nil)]
        [InlineData("false > 1", GlosOp.Gtr, GlosValueType.Boolean, GlosValueType.Integer)]
        [InlineData("{} < true", GlosOp.Lss, GlosValueType.Table, GlosValueType.Boolean)]
        [InlineData("nil >= {}", GlosOp.Geq, GlosValueType.Nil, GlosValueType.Table)]
        [InlineData("true <= 1", GlosOp.Leq, GlosValueType.Boolean, GlosValueType.Integer)]
        public void BadBiOp(string code, GlosOp op, GlosValueType left, GlosValueType right) {
            var exception = Assert.IsType<GlosInvalidBinaryOperandTypeException>(Assert.Throws<GlosRuntimeException>(() => Execute(code)).InnerException);
            Assert.Equal(op, exception.Op);
            Assert.Equal(left, exception.Left.Type);
            Assert.Equal(right, exception.Right.Type);
        }

        [Fact]
        public void Vector() {
            var code = @"
                vector = { 
                    .__add: [a, b] -> vector.new[a.x + b.x, a.y + b.y],
                    .__sub: [a, b] -> vector.new[a.x - b.x, a.y - b.y],
                    .__mul: [a, b] -> a.x * b.x + a.y * b.y,
                    .__lss: [a, b] -> vector.len2$a < vector.len2$b,
                    .__equ: [a, b] -> a.x == b.x & a.y == b.y,

                    .new: [x, y] -> (rv = { .x: x, .y: y }; `rv = vector; rv),
                    .len2: v -> v * v,
                };


                a = vector.new[1, 2];
                b = vector.new[3, 4];
                c = a + b;
                d = a - b;

                [c.x, c.y, d.x, d.y, a * b, a > b, a == b];
            ";

            GlosValueArrayChecker.Create(Execute(code))
                .First().AssertInteger(4)
                .MoveNext().AssertInteger(6)
                .MoveNext().AssertInteger(-2)
                .MoveNext().AssertInteger(-2)
                .MoveNext().AssertInteger(11)
                .MoveNext().AssertFalse()
                .MoveNext().AssertFalse()
                .MoveNext().AssertEnd();
        }
    }
}
