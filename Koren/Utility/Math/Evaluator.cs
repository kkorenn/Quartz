using System.Globalization;

namespace Koren.Utility.Math;

public enum EvalState {
    OK,
    Error,
    Same,
    OverRange,
    UnderRange
}

// Self-contained arithmetic evaluator for slider value fields. Upstream
// Overlayer uses NCalc here; KRP can't take the dependency (single-DLL Mono
// merge — only game-bundled assemblies are referenced), so this is a small
// recursive-descent parser covering what a slider editor actually needs:
// + - * / % ^ , parentheses, unary sign, the constants pi/e/tau, and a handful
// of functions. Case-insensitive, culture-invariant numbers.
public static class Evaluator {
    public static (float result, EvalState state) Evaluate(string exprStr, float currentVal, float? min = null, float? max = null) {
        if(string.IsNullOrWhiteSpace(exprStr)) {
            return (currentVal, EvalState.Error);
        }

        double evaluated;
        try {
            evaluated = new Parser(exprStr).Parse();
        } catch {
            return (currentVal, EvalState.Error);
        }

        if(double.IsNaN(evaluated) || double.IsInfinity(evaluated)) {
            return (currentVal, EvalState.Error);
        }

        float result = (float)evaluated;

        if(min.HasValue && max.HasValue) {
            if(result < min.Value) {
                return (min.Value, EvalState.UnderRange);
            }

            if(result > max.Value) {
                return (max.Value, EvalState.OverRange);
            }
        }

        if(UnityEngine.Mathf.Approximately(result, currentVal)) {
            return (result, EvalState.Same);
        }

        return (result, EvalState.OK);
    }

    // Grammar (precedence low→high):
    //   expr  := term   (('+' | '-') term)*
    //   term  := unary  (('*' | '/' | '%') unary)*
    //   unary := ('+' | '-') unary | power
    //   power := atom   ('^' unary)?            right-associative
    //   atom  := number | ident | '(' expr ')'
    private sealed class Parser {
        private readonly string s;
        private int pos;

        public Parser(string expr) {
            s = expr;
            pos = 0;
        }

        public double Parse() {
            double v = ParseExpr();
            if(Peek() != '\0') {
                throw new FormatException("trailing input");
            }

            return v;
        }

        private char Peek() {
            while(pos < s.Length && char.IsWhiteSpace(s[pos])) {
                pos++;
            }

            return pos < s.Length ? s[pos] : '\0';
        }

        private double ParseExpr() {
            double v = ParseTerm();
            while(true) {
                char c = Peek();
                if(c == '+') {
                    pos++;
                    v += ParseTerm();
                } else if(c == '-') {
                    pos++;
                    v -= ParseTerm();
                } else {
                    return v;
                }
            }
        }

        private double ParseTerm() {
            double v = ParseUnary();
            while(true) {
                char c = Peek();
                if(c == '*') {
                    pos++;
                    v *= ParseUnary();
                } else if(c == '/') {
                    pos++;
                    v /= ParseUnary();
                } else if(c == '%') {
                    pos++;
                    v %= ParseUnary();
                } else {
                    return v;
                }
            }
        }

        private double ParseUnary() {
            char c = Peek();
            if(c == '-') {
                pos++;
                return -ParseUnary();
            }

            if(c == '+') {
                pos++;
                return ParseUnary();
            }

            return ParsePower();
        }

        private double ParsePower() {
            double baseVal = ParseAtom();
            if(Peek() == '^') {
                pos++;
                return System.Math.Pow(baseVal, ParseUnary());
            }

            return baseVal;
        }

        private double ParseAtom() {
            char c = Peek();
            if(c == '(') {
                pos++;
                double v = ParseExpr();
                if(Peek() != ')') {
                    throw new FormatException("missing ')'");
                }

                pos++;
                return v;
            }

            if(char.IsLetter(c)) {
                return ParseIdent();
            }

            return ParseNumber();
        }

        private double ParseNumber() {
            Peek();
            int start = pos;
            while(pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.')) {
                pos++;
            }

            if(pos == start) {
                throw new FormatException("expected number");
            }

            return double.Parse(s.Substring(start, pos - start), CultureInfo.InvariantCulture);
        }

        private double ParseIdent() {
            Peek();
            int start = pos;
            while(pos < s.Length && char.IsLetter(s[pos])) {
                pos++;
            }

            string name = s.Substring(start, pos - start).ToLowerInvariant();

            switch(name) {
                case "pi": return System.Math.PI;
                case "e": return System.Math.E;
                case "tau": return System.Math.PI * 2d;
            }

            if(Peek() != '(') {
                throw new FormatException("unknown identifier '" + name + "'");
            }

            pos++;
            List<double> args = [];
            if(Peek() != ')') {
                args.Add(ParseExpr());
                while(Peek() == ',') {
                    pos++;
                    args.Add(ParseExpr());
                }
            }

            if(Peek() != ')') {
                throw new FormatException("missing ')' for '" + name + "'");
            }

            pos++;
            return ApplyFunc(name, args);
        }

        private static double ApplyFunc(string name, List<double> a) {
            switch(name) {
                case "abs": Require(a, 1); return System.Math.Abs(a[0]);
                case "sqrt": Require(a, 1); return System.Math.Sqrt(a[0]);
                case "sign": Require(a, 1); return System.Math.Sign(a[0]);
                case "floor": Require(a, 1); return System.Math.Floor(a[0]);
                case "ceil": Require(a, 1); return System.Math.Ceiling(a[0]);
                case "round": Require(a, 1); return System.Math.Round(a[0]);
                case "trunc": Require(a, 1); return System.Math.Truncate(a[0]);
                case "exp": Require(a, 1); return System.Math.Exp(a[0]);
                case "ln": Require(a, 1); return System.Math.Log(a[0]);
                case "log10": Require(a, 1); return System.Math.Log10(a[0]);
                case "sin": Require(a, 1); return System.Math.Sin(a[0]);
                case "cos": Require(a, 1); return System.Math.Cos(a[0]);
                case "tan": Require(a, 1); return System.Math.Tan(a[0]);
                case "min": Require(a, 2); return System.Math.Min(a[0], a[1]);
                case "max": Require(a, 2); return System.Math.Max(a[0], a[1]);
                case "pow": Require(a, 2); return System.Math.Pow(a[0], a[1]);
                case "log": Require(a, 2); return System.Math.Log(a[0], a[1]);
                case "clamp": Require(a, 3); return System.Math.Min(System.Math.Max(a[0], a[1]), a[2]);
                default: throw new FormatException("unknown function '" + name + "'");
            }
        }

        private static void Require(List<double> a, int n) {
            if(a.Count != n) {
                throw new FormatException("wrong argument count");
            }
        }
    }
}
