using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using DSAnimStudio;
using DSAnimStudio.TaeEditor;

namespace DSAnimStudio.TaeEditor.TaeScripting
{
    public class Token
    {
        public string Type { get; set; }
        public string Value { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }

        public Token(string type, string value, int line, int column)
        {
            Type = type;
            Value = value;
            Line = line;
            Column = column;
        }

        public override string ToString()
        {
            return $"Token({Type}, {repr(Value)}, {Line}:{Column})";
        }

        private string repr(string val)
        {
            if (val == null) return "null";
            return $"\"{val.Replace("\"", "\\\"")}\"";
        }
    }

    public static class TaeScriptLexer
    {
        public static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "MATCH", "anim", "ADD", "REMOVE", "MODIFY", "SHIFT", "AT", "COPY", "TIMING", 
            "OF", "WHERE", "SET", "BY", "FROM", "START", "END", "TO", "ALL", "args", 
            "AND", "OR", "NOT", "True", "False", "M", "start", "end"
        };

        private static readonly List<(string name, string pattern)> TokenDefs = new List<(string, string)>
        {
            ("COMMENT",   @"#[^\n]*"),
            ("STRING",    @"""[^""\\]*(?:\\.[^""\\]*)*"""),
            ("RANGE",     @"[0-9M]+-[0-9M]+"),
            ("COMP_OP",   @"==|!=|>=|<=|>|<"),
            ("ASSIGN",    @"="),
            ("PLUS",      @"\+"),
            ("MINUS",     @"-"),
            ("COMMA",     @","),
            ("SEMICOLON", @";"),
            ("LBRACE",    @"\{"),
            ("RBRACE",    @"\}"),
            ("LBRACKET",  @"\["),
            ("RBRACKET",  @"\]"),
            ("LPAREN",    @"\("),
            ("RPAREN",    @"\)"),
            ("NUMBER",    @"\d+(?:\.\d+)?"),
            ("IDENT",     @"[a-zA-Z_][a-zA-Z0-9_<>\-]*"),
        };

        private static readonly Regex MasterRegex = new Regex(
            string.Join("|", TokenDefs.Select(d => $"(?<{d.name}>{d.pattern})")),
            RegexOptions.Compiled
        );

        public static List<Token> Lex(string code)
        {
            var tokens = new List<Token>();
            int currentPos = 0;
            int lineNum = 1;
            int lineStart = 0;

            while (currentPos < code.Length)
            {
                char c = code[currentPos];
                if (char.IsWhiteSpace(c))
                {
                    if (c == '\n')
                    {
                        lineNum++;
                        lineStart = currentPos + 1;
                    }
                    currentPos++;
                    continue;
                }

                var match = MasterRegex.Match(code, currentPos);
                if (!match.Success || match.Index != currentPos)
                {
                    throw new SyntaxException($"Lexer Error: Unexpected character '{c}' at line {lineNum}, column {currentPos - lineStart + 1}");
                }

                string matchedKind = null;
                string matchedValue = null;

                foreach (var def in TokenDefs)
                {
                    var group = match.Groups[def.name];
                    if (group.Success)
                    {
                        matchedKind = def.name;
                        matchedValue = group.Value;
                        break;
                    }
                }

                int col = currentPos - lineStart + 1;
                currentPos += match.Length;

                if (matchedKind == "COMMENT")
                    continue;

                if (matchedKind == "IDENT")
                {
                    if (Keywords.Contains(matchedValue))
                    {
                        matchedKind = matchedValue; // Token type is the exact keyword string
                    }
                }

                tokens.Add(new Token(matchedKind, matchedValue, lineNum, col));
            }

            return tokens;
        }
    }

    public class SyntaxException : Exception
    {
        public SyntaxException(string msg) : base(msg) { }
    }

    public class TaeScriptParser
    {
        private readonly List<Token> tokens;
        private int pos = 0;

        public TaeScriptParser(List<Token> tokens)
        {
            this.tokens = tokens;
        }

        private Token Peek(int offset = 0)
        {
            if (pos + offset < tokens.Count)
                return tokens[pos + offset];
            return null;
        }

        private Token Match(params string[] expectedTypes)
        {
            var tok = Peek();
            if (tok != null && expectedTypes.Contains(tok.Type))
            {
                pos++;
                return tok;
            }
            return null;
        }

        private Token Consume(string expectedType, string errMsg = null)
        {
            var tok = Match(expectedType);
            if (tok != null)
                return tok;

            var curr = Peek();
            string loc = curr != null ? $"at line {curr.Line}, col {curr.Column}" : "at EOF";
            string msg = errMsg ?? $"Parser Error: Expected token {expectedType}, got {curr?.Type ?? "EOF"} ({loc})";
            throw new SyntaxException(msg);
        }

        public ProgramNode ParseProgram()
        {
            var blocks = new List<MatchBlockNode>();
            while (Peek() != null)
            {
                blocks.Add(ParseMatchBlock());
            }
            return new ProgramNode { Blocks = blocks };
        }

        private MatchBlockNode ParseMatchBlock()
        {
            Consume("MATCH", "Expected 'MATCH'");
            Consume("anim", "Expected 'anim'");

            var patterns = new List<string>();
            var pat = Consume("STRING", "Expected pattern string");
            patterns.Add(pat.Value.Trim('"'));

            while (Match("COMMA") != null)
            {
                pat = Consume("STRING", "Expected pattern string after comma");
                patterns.Add(pat.Value.Trim('"'));
            }

            Consume("LBRACE", "Expected '{' to start MATCH block");

            var statements = new List<StatementNode>();
            while (Match("RBRACE") == null)
            {
                statements.Add(ParseStatement());
            }

            return new MatchBlockNode { Patterns = patterns, Body = statements };
        }

        private StatementNode ParseStatement()
        {
            var tok = Peek();
            if (tok == null)
                throw new SyntaxException("Parser Error: Unexpected EOF while parsing statement");

            if (tok.Type == "ADD")
                return ParseAddStmt();
            else if (tok.Type == "REMOVE")
                return ParseRemoveStmt();
            else if (tok.Type == "MODIFY")
                return ParseModifyStmt();
            else if (tok.Type == "SHIFT")
                return ParseShiftStmt();
            else
                throw new SyntaxException($"Parser Error: Unexpected token '{tok.Value}' at line {tok.Line}, col {tok.Column}");
        }

        private AddStatementNode ParseAddStmt()
        {
            Consume("ADD");
            var eventStr = Consume("STRING", "Expected event signature string after ADD");
            Consume("AT", "Expected 'AT' after event string");
            var timing = ParseTimingExpr();
            Consume("SEMICOLON", "Expected ';' at the end of ADD statement");

            return new AddStatementNode
            {
                EventSignature = eventStr.Value.Trim('"'),
                Timing = timing
            };
        }

        private RemoveStatementNode ParseRemoveStmt()
        {
            Consume("REMOVE");
            var target = Consume("STRING", "Expected target event selector string after REMOVE");
            ConditionNode cond = null;
            if (Match("WHERE") != null)
            {
                cond = ParseCondition();
            }
            Consume("SEMICOLON", "Expected ';' at the end of REMOVE statement");

            return new RemoveStatementNode
            {
                Target = target.Value.Trim('"'),
                Condition = cond
            };
        }

        private ModifyStatementNode ParseModifyStmt()
        {
            Consume("MODIFY");
            var target = Consume("STRING", "Expected target event selector string after MODIFY");
            ConditionNode cond = null;
            if (Match("WHERE") != null)
            {
                cond = ParseCondition();
            }
            Consume("SET", "Expected 'SET' in MODIFY statement");

            var assignments = new List<AssignmentNode>();
            assignments.Add(ParseAssignment());
            while (Match("COMMA") != null)
            {
                assignments.Add(ParseAssignment());
            }

            Consume("SEMICOLON", "Expected ';' at the end of MODIFY statement");

            return new ModifyStatementNode
            {
                Target = target.Value.Trim('"'),
                Condition = cond,
                Assignments = assignments
            };
        }

        private StatementNode ParseShiftStmt()
        {
            Consume("SHIFT");
            if (Match("ALL") != null)
            {
                Consume("BY", "Expected 'BY' after SHIFT ALL");
                int offset = ParseSignedInteger();
                Consume("SEMICOLON", "Expected ';'");
                return new ShiftAllStatementNode
                {
                    Offset = offset
                };
            }
            else
            {
                var target = Consume("STRING", "Expected target event selector or 'ALL' after SHIFT");
                ConditionNode cond = null;
                if (Match("WHERE") != null)
                {
                    cond = ParseCondition();
                }
                Consume("BY", "Expected 'BY' after SHIFT target");
                int offset = ParseSignedInteger();
                Consume("SEMICOLON", "Expected ';'");

                return new ShiftStatementNode
                {
                    Target = target.Value.Trim('"'),
                    Condition = cond,
                    Offset = offset
                };
            }
        }

        private AssignmentNode ParseAssignment()
        {
            var tok = Peek();
            if (tok == null)
                throw new SyntaxException("Parser Error: Unexpected EOF inside SET clause");

            if (tok.Type == "args")
            {
                Consume("args");
                Consume("LBRACKET");
                var idxTok = Consume("NUMBER", "Expected index number inside args[...] ");
                int idx = int.Parse(idxTok.Value);
                Consume("RBRACKET");
                Consume("ASSIGN");
                var expr = ParseExpr();
                return new AssignmentNode { Target = "arg", ArgIndex = idx, ValueExpr = expr };
            }
            else if (tok.Type == "start")
            {
                Consume("start");
                Consume("ASSIGN");
                var expr = ParseExpr();
                return new AssignmentNode { Target = "start", ValueExpr = expr };
            }
            else if (tok.Type == "end")
            {
                Consume("end");
                Consume("ASSIGN");
                var expr = ParseExpr();
                return new AssignmentNode { Target = "end", ValueExpr = expr };
            }
            else
            {
                throw new SyntaxException($"Parser Error: Expected 'args', 'start', or 'end' in assignment, got '{tok.Value}'");
            }
        }

        private ExprNode ParseExpr()
        {
            var term = ParseTerm();
            while (true)
            {
                var op = Match("PLUS", "MINUS");
                if (op == null)
                    break;
                var nextTerm = ParseTerm();
                term = new BinaryExprNode { Left = term, Operator = op.Value, Right = nextTerm };
            }
            return term;
        }

        private ExprNode ParseTerm()
        {
            bool minus = Match("MINUS") != null;
            var tok = Peek();
            if (tok == null)
                throw new SyntaxException("Parser Error: Unexpected EOF while parsing expression term");

            if (tok.Type == "NUMBER")
            {
                Consume("NUMBER");
                double val = double.Parse(tok.Value, CultureInfo.InvariantCulture);
                if (minus) val = -val;
                return new LiteralNode { Value = val };
            }

            if (minus)
                throw new SyntaxException($"Parser Error: Unexpected '-' before non-number token '{tok.Value}'");

            if (tok.Type == "STRING")
            {
                Consume("STRING");
                return new LiteralNode { Value = tok.Value.Trim('"') };
            }
            else if (tok.Type == "True")
            {
                Consume("True");
                return new LiteralNode { Value = true };
            }
            else if (tok.Type == "False")
            {
                Consume("False");
                return new LiteralNode { Value = false };
            }
            else if (tok.Type == "M")
            {
                Consume("M");
                return new LiteralNode { Value = "M" };
            }
            else if (tok.Type == "start")
            {
                Consume("start");
                return new VariableNode { Name = "start" };
            }
            else if (tok.Type == "end")
            {
                Consume("end");
                return new VariableNode { Name = "end" };
            }
            else if (tok.Type == "args")
            {
                Consume("args");
                Consume("LBRACKET");
                var idxTok = Consume("NUMBER", "Expected index number inside args[...] ");
                int idx = int.Parse(idxTok.Value);
                Consume("RBRACKET");
                return new ArgumentRefNode { Index = idx };
            }
            else
            {
                throw new SyntaxException($"Parser Error: Unexpected token in expression: '{tok.Value}'");
            }
        }

        private TimingExprNode ParseTimingExpr()
        {
            if (Match("COPY") != null)
            {
                Consume("TIMING");
                Consume("OF");
                var target = Consume("STRING", "Expected target event string after COPY TIMING OF");
                ConditionNode cond = null;
                if (Match("WHERE") != null)
                {
                    cond = ParseCondition();
                }
                return new CopyTimingNode
                {
                    Target = target.Value.Trim('"'),
                    Condition = cond
                };
            }
            else if (Match("FROM") != null)
            {
                var startPoint = ParseTimePoint();
                Consume("TO", "Expected 'TO' in FROM-TO timing expression");
                var endPoint = ParseTimePoint();
                return new FromToTimingNode
                {
                    StartPoint = startPoint,
                    EndPoint = endPoint
                };
            }
            else
            {
                var rangeTok = Consume("RANGE", "Expected timing range (e.g., 35-80, 0-M) or COPY/FROM");
                var parts = rangeTok.Value.Split('-');
                return new AbsoluteRangeNode
                {
                    StartVal = parts[0],
                    EndVal = parts[1]
                };
            }
        }

        private TimePointNode ParseTimePoint()
        {
            if (Match("START") != null)
            {
                Consume("OF");
                var target = Consume("STRING", "Expected target event string after START OF");
                ConditionNode cond = null;
                if (Match("WHERE") != null)
                {
                    cond = ParseCondition();
                }
                int offset = ParseOptionalOffset();
                return new TimePointNode
                {
                    Reference = "start_of",
                    Target = target.Value.Trim('"'),
                    Condition = cond,
                    Offset = offset
                };
            }
            else if (Match("END") != null)
            {
                Consume("OF");
                var target = Consume("STRING", "Expected target event string after END OF");
                ConditionNode cond = null;
                if (Match("WHERE") != null)
                {
                    cond = ParseCondition();
                }
                int offset = ParseOptionalOffset();
                return new TimePointNode
                {
                    Reference = "end_of",
                    Target = target.Value.Trim('"'),
                    Condition = cond,
                    Offset = offset
                };
            }
            else if (Peek()?.Type == "M")
            {
                Consume("M");
                return new TimePointNode { Reference = "M" };
            }
            else if (Peek()?.Type == "NUMBER")
            {
                var numTok = Consume("NUMBER");
                return new TimePointNode { Reference = "literal", LiteralValue = numTok.Value };
            }
            else
            {
                bool minus = Match("MINUS") != null;
                var numTok = Consume("NUMBER", "Expected number or M or START/END OF");
                string val = minus ? "-" + numTok.Value : numTok.Value;
                return new TimePointNode { Reference = "literal", LiteralValue = val };
            }
        }

        private int ParseOptionalOffset()
        {
            var sign = Match("PLUS", "MINUS");
            if (sign != null)
            {
                var num = Consume("NUMBER", "Expected number after +/- sign");
                int val = (int)double.Parse(num.Value, CultureInfo.InvariantCulture);
                if (sign.Type == "MINUS")
                    val = -val;
                return val;
            }
            return 0;
        }

        private int ParseSignedInteger()
        {
            bool minus = Match("MINUS") != null;
            Match("PLUS"); // match optional plus
            var num = Consume("NUMBER", "Expected offset integer");
            int val = (int)double.Parse(num.Value, CultureInfo.InvariantCulture);
            if (minus)
                val = -val;
            return val;
        }

        private ConditionNode ParseCondition()
        {
            return ParseOrCondition();
        }

        private ConditionNode ParseOrCondition()
        {
            var node = ParseAndCondition();
            while (Match("OR") != null)
            {
                var right = ParseAndCondition();
                node = new LogicalConditionNode { Left = node, Operator = "OR", Right = right };
            }
            return node;
        }

        private ConditionNode ParseAndCondition()
        {
            var node = ParseComparison();
            while (Match("AND") != null)
            {
                var right = ParseComparison();
                node = new LogicalConditionNode { Left = node, Operator = "AND", Right = right };
            }
            return node;
        }

        private ConditionNode ParseComparison()
        {
            if (Match("LPAREN") != null)
            {
                var cond = ParseCondition();
                Consume("RPAREN", "Expected ')' after condition");
                return cond;
            }

            var tok = Peek();
            if (tok == null)
                throw new SyntaxException("Parser Error: Unexpected EOF while parsing condition");

            if (tok.Type == "args")
            {
                Consume("args");
                Consume("LBRACKET");
                var idxTok = Consume("NUMBER", "Expected index number inside args[...] ");
                int idx = int.Parse(idxTok.Value);
                Consume("RBRACKET");
                var opTok = Consume("COMP_OP", "Expected comparison operator (==, !=, >, <, >=, <=)");
                var val = ParseValueLiteral();
                return new ComparisonConditionNode
                {
                    Left = new ArgumentRefNode { Index = idx },
                    Operator = opTok.Value,
                    Right = val
                };
            }
            else if (tok.Type == "start")
            {
                Consume("start");
                var opTok = Consume("COMP_OP", "Expected comparison operator");
                var val = ParseTimingValue();
                return new ComparisonConditionNode
                {
                    Left = new VariableNode { Name = "start" },
                    Operator = opTok.Value,
                    Right = val
                };
            }
            else if (tok.Type == "end")
            {
                Consume("end");
                var opTok = Consume("COMP_OP", "Expected comparison operator");
                var val = ParseTimingValue();
                return new ComparisonConditionNode
                {
                    Left = new VariableNode { Name = "end" },
                    Operator = opTok.Value,
                    Right = val
                };
            }
            else
            {
                throw new SyntaxException($"Parser Error: Expected 'args', 'start', or 'end' in comparison, got '{tok.Value}'");
            }
        }

        private ExprNode ParseValueLiteral()
        {
            bool minus = Match("MINUS") != null;
            var tok = Peek();
            if (tok == null)
                throw new SyntaxException("Parser Error: Unexpected EOF while parsing literal");

            if (tok.Type == "NUMBER")
            {
                Consume("NUMBER");
                double val = double.Parse(tok.Value, CultureInfo.InvariantCulture);
                return new LiteralNode { Value = minus ? -val : val };
            }

            if (minus)
                throw new SyntaxException($"Parser Error: Unexpected '-' before '{tok.Value}'");

            if (tok.Type == "STRING")
            {
                Consume("STRING");
                return new LiteralNode { Value = tok.Value.Trim('"') };
            }
            else if (tok.Type == "True")
            {
                Consume("True");
                return new LiteralNode { Value = true };
            }
            else if (tok.Type == "False")
            {
                Consume("False");
                return new LiteralNode { Value = false };
            }
            else if (tok.Type == "M")
            {
                Consume("M");
                return new LiteralNode { Value = "M" };
            }
            else
            {
                throw new SyntaxException($"Parser Error: Expected literal value (number, string, boolean), got '{tok.Value}'");
            }
        }

        private ExprNode ParseTimingValue()
        {
            var tok = Peek();
            if (tok != null && tok.Type == "M")
            {
                Consume("M");
                return new LiteralNode { Value = "M" };
            }
            else if (tok != null && tok.Type == "NUMBER")
            {
                Consume("NUMBER");
                return new LiteralNode { Value = double.Parse(tok.Value, CultureInfo.InvariantCulture) };
            }
            else
            {
                bool minus = Match("MINUS") != null;
                var num = Consume("NUMBER", "Expected number or M");
                double val = double.Parse(num.Value, CultureInfo.InvariantCulture);
                return new LiteralNode { Value = minus ? -val : val };
            }
        }
    }

    // AST Nodes
    public abstract class ASTNode { }

    public class ProgramNode : ASTNode
    {
        public List<MatchBlockNode> Blocks { get; set; } = new List<MatchBlockNode>();
    }

    public class MatchBlockNode : ASTNode
    {
        public List<string> Patterns { get; set; } = new List<string>();
        public List<StatementNode> Body { get; set; } = new List<StatementNode>();
    }

    public abstract class StatementNode : ASTNode
    {
        public abstract void Execute(DSAProj.Animation anim, float frameDuration, double maxTime, Dictionary<int, SoulsAssetPipeline.Animation.TAE.Template.ActionTemplate> templates, ref int addedCount, ref int removedCount, ref int modifiedCount, ref int shiftedCount, List<string> logs);

        protected bool MatchEventSignature(DSAProj.Action ev, string target)
        {
            if (int.TryParse(target, out int targetId))
            {
                return ev.Type == targetId;
            }

            if (ev.Template != null && ev.Template.Name != null)
            {
                return ev.Template.Name.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                       ev.Template.Name.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return false;
        }
    }

    public abstract class ConditionNode : ASTNode
    {
        public abstract bool Evaluate(DSAProj.Action ev, float frameDuration, double maxTime);
    }

    public class LogicalConditionNode : ConditionNode
    {
        public ConditionNode Left { get; set; }
        public string Operator { get; set; } // "AND" or "OR"
        public ConditionNode Right { get; set; }

        public override bool Evaluate(DSAProj.Action ev, float frameDuration, double maxTime)
        {
            if (Operator == "AND")
                return Left.Evaluate(ev, frameDuration, maxTime) && Right.Evaluate(ev, frameDuration, maxTime);
            else
                return Left.Evaluate(ev, frameDuration, maxTime) || Right.Evaluate(ev, frameDuration, maxTime);
        }
    }

    public class ComparisonConditionNode : ConditionNode
    {
        public ExprNode Left { get; set; }
        public string Operator { get; set; } // "==", "!=", ">", "<", ">=", "<="
        public ExprNode Right { get; set; }

        public override bool Evaluate(DSAProj.Action ev, float frameDuration, double maxTime)
        {
            object lVal = Left.Evaluate(ev, frameDuration, maxTime);
            object rVal = Right.Evaluate(ev, frameDuration, maxTime);
            return CompareValues(lVal, Operator, rVal);
        }

        private bool CompareValues(object l, string op, object r)
        {
            if (l == null || r == null)
                return op == "!=";

            // Convert to common type if numeric
            if (IsNumeric(l) || IsNumeric(r))
            {
                double dl = Convert.ToDouble(l);
                double dr = Convert.ToDouble(r);
                switch (op)
                {
                    case "==": return Math.Abs(dl - dr) < 0.0001;
                    case "!=": return Math.Abs(dl - dr) >= 0.0001;
                    case ">": return dl > dr;
                    case "<": return dl < dr;
                    case ">=": return dl >= dr;
                    case "<=": return dl <= dr;
                }
            }
            else if (l is bool bl && r is bool br)
            {
                switch (op)
                {
                    case "==": return bl == br;
                    case "!=": return bl != br;
                }
            }
            else
            {
                string sl = l.ToString();
                string sr = r.ToString();
                switch (op)
                {
                    case "==": return sl.Equals(sr, StringComparison.OrdinalIgnoreCase);
                    case "!=": return !sl.Equals(sr, StringComparison.OrdinalIgnoreCase);
                }
            }
            return false;
        }

        private bool IsNumeric(object o)
        {
            return o is double || o is float || o is int || o is long || o is short || o is byte || o is sbyte || o is uint || o is ushort || o is ulong;
        }
    }

    public abstract class ExprNode : ASTNode
    {
        public abstract object Evaluate(DSAProj.Action ev, float frameDuration, double maxTime);
    }

    public class LiteralNode : ExprNode
    {
        public object Value { get; set; }

        public override object Evaluate(DSAProj.Action ev, float frameDuration, double maxTime)
        {
            if (Value is string s && s == "M")
            {
                return Math.Round(maxTime / frameDuration);
            }
            return Value;
        }
    }

    public class VariableNode : ExprNode
    {
        public string Name { get; set; } // "start" or "end"

        public override object Evaluate(DSAProj.Action ev, float frameDuration, double maxTime)
        {
            if (Name == "start")
                return (double)Math.Round(ev.StartTime / frameDuration);
            else if (Name == "end")
                return (double)Math.Round(ev.EndTime / frameDuration);
            return 0.0;
        }
    }

    public class ArgumentRefNode : ExprNode
    {
        public int Index { get; set; }

        public override object Evaluate(DSAProj.Action ev, float frameDuration, double maxTime)
        {
            if (ev.Parameters == null && ev.Template != null)
            {
                ev.NewLoadParamsFromBytes(true);
            }

            if (ev.Parameters != null && Index >= 0 && Index < ev.Parameters.Values.Count)
            {
                return ev.Parameters.Values[Index];
            }
            return 0;
        }
    }

    public class BinaryExprNode : ExprNode
    {
        public ExprNode Left { get; set; }
        public string Operator { get; set; } // "+" or "-"
        public ExprNode Right { get; set; }

        public override object Evaluate(DSAProj.Action ev, float frameDuration, double maxTime)
        {
            double lVal = Convert.ToDouble(Left.Evaluate(ev, frameDuration, maxTime));
            double rVal = Convert.ToDouble(Right.Evaluate(ev, frameDuration, maxTime));
            if (Operator == "+")
                return lVal + rVal;
            else
                return lVal - rVal;
        }
    }

    public abstract class TimingExprNode : ASTNode
    {
        public abstract (float start, float end) GetTiming(DSAProj.Animation anim, float frameDuration, double maxTime);

        protected bool MatchEventSignature(DSAProj.Action ev, string target)
        {
            if (int.TryParse(target, out int targetId))
            {
                return ev.Type == targetId;
            }

            if (ev.Template != null && ev.Template.Name != null)
            {
                return ev.Template.Name.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                       ev.Template.Name.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return false;
        }
    }

    public class AbsoluteRangeNode : TimingExprNode
    {
        public string StartVal { get; set; } // number or "M"
        public string EndVal { get; set; }   // number or "M"

        public override (float start, float end) GetTiming(DSAProj.Animation anim, float frameDuration, double maxTime)
        {
            float s = 0;
            if (StartVal == "M")
                s = (float)maxTime;
            else
                s = float.Parse(StartVal) * frameDuration;

            float e = 0;
            if (EndVal == "M")
                e = (float)maxTime;
            else
                e = float.Parse(EndVal) * frameDuration;

            return (s, e);
        }
    }

    public class CopyTimingNode : TimingExprNode
    {
        public string Target { get; set; }
        public ConditionNode Condition { get; set; }

        public override (float start, float end) GetTiming(DSAProj.Animation anim, float frameDuration, double maxTime)
        {
            var matchedEvent = anim.SAFE_GetActions().FirstOrDefault(ev => MatchEventSignature(ev, Target) && (Condition == null || Condition.Evaluate(ev, frameDuration, maxTime)));
            if (matchedEvent != null)
            {
                return (matchedEvent.StartTime, matchedEvent.EndTime);
            }
            return (0f, 0f);
        }
    }

    public class FromToTimingNode : TimingExprNode
    {
        public TimePointNode StartPoint { get; set; }
        public TimePointNode EndPoint { get; set; }

        public override (float start, float end) GetTiming(DSAProj.Animation anim, float frameDuration, double maxTime)
        {
            float s = StartPoint.EvaluateTime(anim, frameDuration, maxTime);
            float e = EndPoint.EvaluateTime(anim, frameDuration, maxTime);
            return (s, e);
        }
    }

    public class TimePointNode : ASTNode
    {
        public string Reference { get; set; } // "start_of", "end_of", "literal", "M"
        public string Target { get; set; }    // StringLiteral for reference
        public ConditionNode Condition { get; set; }
        public int Offset { get; set; }       // frame offset
        public string LiteralValue { get; set; } // number string

        public float EvaluateTime(DSAProj.Animation anim, float frameDuration, double maxTime)
        {
            if (Reference == "M")
                return (float)maxTime;
            if (Reference == "literal")
                return float.Parse(LiteralValue) * frameDuration;

            var matchedEvent = anim.SAFE_GetActions().FirstOrDefault(ev => MatchEventSignature(ev, Target) && (Condition == null || Condition.Evaluate(ev, frameDuration, maxTime)));
            if (matchedEvent != null)
            {
                float baseTime = (Reference == "start_of") ? matchedEvent.StartTime : matchedEvent.EndTime;
                return baseTime + (Offset * frameDuration);
            }
            return 0f;
        }

        private bool MatchEventSignature(DSAProj.Action ev, string target)
        {
            if (int.TryParse(target, out int targetId))
            {
                return ev.Type == targetId;
            }

            if (ev.Template != null && ev.Template.Name != null)
            {
                return ev.Template.Name.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                       ev.Template.Name.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return false;
        }
    }

    public class AddStatementNode : StatementNode
    {
        public string EventSignature { get; set; }
        public TimingExprNode Timing { get; set; }

        public override void Execute(DSAProj.Animation anim, float frameDuration, double maxTime, Dictionary<int, SoulsAssetPipeline.Animation.TAE.Template.ActionTemplate> templates, ref int addedCount, ref int removedCount, ref int modifiedCount, ref int shiftedCount, List<string> logs)
        {
            int eventType = -1;
            if (int.TryParse(EventSignature, out int idVal))
            {
                eventType = idVal;
            }
            else
            {
                var matchingTemplate = templates.Values.FirstOrDefault(t => t.Name != null && t.Name.Equals(EventSignature, StringComparison.OrdinalIgnoreCase));
                if (matchingTemplate == null)
                {
                    matchingTemplate = templates.Values.FirstOrDefault(t => t.Name != null && t.Name.IndexOf(EventSignature, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                if (matchingTemplate != null)
                {
                    eventType = matchingTemplate.ID;
                }
                else
                {
                    logs.Add($"Warning: Could not find event template for '{EventSignature}'. Add statement skipped.");
                    return;
                }
            }

            var (s, e) = Timing.GetTiming(anim, frameDuration, maxTime);

            SoulsAssetPipeline.Animation.TAE.Template.ActionTemplate evTemplate = null;
            if (templates.ContainsKey(eventType))
                evTemplate = templates[eventType];

            byte[] defaultBytes = evTemplate != null ? evTemplate.GetDefaultBytes() : new byte[16];

            // Always append a brand-new track
            int trackIdx = anim.SAFE_GetActionTrackCount();
            anim.SAFE_EnsureMinimumActionTrackCount(trackIdx + 1);

            // Name the new track after the action's template name
            var newTrack = anim.SAFE_GetActionTrackByIndex(trackIdx);
            if (newTrack != null)
            {
                string trackName = evTemplate?.Name ?? eventType.ToString();
                newTrack.Info.DisplayName = trackName;
            }

            var newAct = new DSAProj.Action()
            {
                StartTime = s,
                EndTime = e,
                ParameterBytes = defaultBytes,
                Type = eventType,
                TrackIndex = trackIdx,
            };

            if (evTemplate != null)
            {
                newAct.ApplyTemplate(evTemplate);
            }

            anim.SAFE_AddAction(newAct);
            anim.SAFE_SetIsModified(true);
            addedCount++;
        }
    }

    public class RemoveStatementNode : StatementNode
    {
        public string Target { get; set; }
        public ConditionNode Condition { get; set; }

        public override void Execute(DSAProj.Animation anim, float frameDuration, double maxTime, Dictionary<int, SoulsAssetPipeline.Animation.TAE.Template.ActionTemplate> templates, ref int addedCount, ref int removedCount, ref int modifiedCount, ref int shiftedCount, List<string> logs)
        {
            var actions = anim.SAFE_GetActions();
            var toRemove = new List<DSAProj.Action>();

            foreach (var ev in actions)
            {
                if (MatchEventSignature(ev, Target))
                {
                    if (Condition == null || Condition.Evaluate(ev, frameDuration, maxTime))
                    {
                        toRemove.Add(ev);
                    }
                }
            }

            foreach (var ev in toRemove)
            {
                anim.SAFE_RemoveAction(ev);
                removedCount++;
            }

            if (toRemove.Count > 0)
            {
                anim.SAFE_SetIsModified(true);
            }
        }
    }

    public class AssignmentNode : ASTNode
    {
        public string Target { get; set; } // "start", "end", "arg"
        public int ArgIndex { get; set; }  // used if Target == "arg"
        public ExprNode ValueExpr { get; set; }
    }

    public class ModifyStatementNode : StatementNode
    {
        public string Target { get; set; }
        public ConditionNode Condition { get; set; }
        public List<AssignmentNode> Assignments { get; set; } = new List<AssignmentNode>();

        public override void Execute(DSAProj.Animation anim, float frameDuration, double maxTime, Dictionary<int, SoulsAssetPipeline.Animation.TAE.Template.ActionTemplate> templates, ref int addedCount, ref int removedCount, ref int modifiedCount, ref int shiftedCount, List<string> logs)
        {
            var actions = anim.SAFE_GetActions();
            bool modifiedAny = false;

            foreach (var ev in actions)
            {
                if (MatchEventSignature(ev, Target))
                {
                    if (Condition == null || Condition.Evaluate(ev, frameDuration, maxTime))
                    {
                        if (ev.Parameters == null && ev.Template != null)
                        {
                            ev.NewLoadParamsFromBytes(true);
                        }

                        foreach (var assign in Assignments)
                        {
                            object val = assign.ValueExpr.Evaluate(ev, frameDuration, maxTime);
                            if (assign.Target == "start")
                            {
                                float newStart = Convert.ToSingle(val) * frameDuration;
                                ev.StartTime = newStart;
                            }
                            else if (assign.Target == "end")
                            {
                                float newEnd = Convert.ToSingle(val) * frameDuration;
                                ev.EndTime = newEnd;
                            }
                            else if (assign.Target == "arg")
                            {
                                if (ev.Parameters != null && assign.ArgIndex >= 0 && assign.ArgIndex < ev.Parameters.Values.Count)
                                {
                                    object convertedVal = ConvertType(val, ev.Parameters.Template[assign.ArgIndex].ParamType);
                                    ev.Parameters[assign.ArgIndex] = convertedVal;
                                }
                                else
                                {
                                    logs.Add($"Warning: Argument index {assign.ArgIndex} out of bounds for event type {ev.Type}.");
                                }
                            }
                        }

                        if (ev.Parameters != null)
                        {
                            ev.ParameterBytes = ev.Parameters.AsBytes(false);
                        }

                        ev.NeedsTextRefresh = true;
                        modifiedAny = true;
                        modifiedCount++;
                    }
                }
            }

            if (modifiedAny)
            {
                anim.SAFE_SetIsModified(true);
            }
        }

        private object ConvertType(object val, SoulsAssetPipeline.Animation.TAE.Template.ParamTypes type)
        {
            try
            {
                switch (type)
                {
                    case SoulsAssetPipeline.Animation.TAE.Template.ParamTypes.b:
                        return Convert.ToBoolean(val);
                    case SoulsAssetPipeline.Animation.TAE.Template.ParamTypes.u8:
                    case SoulsAssetPipeline.Animation.TAE.Template.ParamTypes.x8:
                        return Convert.ToByte(val);
                    case SoulsAssetPipeline.Animation.TAE.Template.ParamTypes.s8:
                        return Convert.ToSByte(val);
                    case SoulsAssetPipeline.Animation.TAE.Template.ParamTypes.u16:
                    case SoulsAssetPipeline.Animation.TAE.Template.ParamTypes.x16:
                        return Convert.ToUInt16(val);
                    case SoulsAssetPipeline.Animation.TAE.Template.ParamTypes.s16:
                        return Convert.ToInt16(val);
                    case SoulsAssetPipeline.Animation.TAE.Template.ParamTypes.u32:
                    case SoulsAssetPipeline.Animation.TAE.Template.ParamTypes.x32:
                        return Convert.ToUInt32(val);
                    case SoulsAssetPipeline.Animation.TAE.Template.ParamTypes.s32:
                        return Convert.ToInt32(val);
                    case SoulsAssetPipeline.Animation.TAE.Template.ParamTypes.u64:
                    case SoulsAssetPipeline.Animation.TAE.Template.ParamTypes.x64:
                        return Convert.ToUInt64(val);
                    case SoulsAssetPipeline.Animation.TAE.Template.ParamTypes.s64:
                        return Convert.ToInt64(val);
                    case SoulsAssetPipeline.Animation.TAE.Template.ParamTypes.f32:
                        return Convert.ToSingle(val);
                    case SoulsAssetPipeline.Animation.TAE.Template.ParamTypes.f64:
                        return Convert.ToDouble(val);
                }
            }
            catch
            {
                // Fallback
            }
            return val;
        }
    }

    public class ShiftStatementNode : StatementNode
    {
        public string Target { get; set; }
        public ConditionNode Condition { get; set; }
        public int Offset { get; set; }

        public override void Execute(DSAProj.Animation anim, float frameDuration, double maxTime, Dictionary<int, SoulsAssetPipeline.Animation.TAE.Template.ActionTemplate> templates, ref int addedCount, ref int removedCount, ref int modifiedCount, ref int shiftedCount, List<string> logs)
        {
            var actions = anim.SAFE_GetActions();
            bool shiftedAny = false;

            foreach (var ev in actions)
            {
                if (MatchEventSignature(ev, Target))
                {
                    if (Condition == null || Condition.Evaluate(ev, frameDuration, maxTime))
                    {
                        ev.StartTime += Offset * frameDuration;
                        ev.EndTime += Offset * frameDuration;
                        ev.NeedsTextRefresh = true;
                        shiftedAny = true;
                        shiftedCount++;
                    }
                }
            }

            if (shiftedAny)
            {
                anim.SAFE_SetIsModified(true);
            }
        }
    }

    public class ShiftAllStatementNode : StatementNode
    {
        public int Offset { get; set; }

        public override void Execute(DSAProj.Animation anim, float frameDuration, double maxTime, Dictionary<int, SoulsAssetPipeline.Animation.TAE.Template.ActionTemplate> templates, ref int addedCount, ref int removedCount, ref int modifiedCount, ref int shiftedCount, List<string> logs)
        {
            var actions = anim.SAFE_GetActions();
            bool shiftedAny = false;

            foreach (var ev in actions)
            {
                ev.StartTime += Offset * frameDuration;
                ev.EndTime += Offset * frameDuration;
                ev.NeedsTextRefresh = true;
                shiftedAny = true;
                shiftedCount++;
            }

            if (shiftedAny)
            {
                anim.SAFE_SetIsModified(true);
            }
        }
    }

    public enum ExecutionScope
    {
        CurrentAnimation,
        SelectedCategory,
        AllAnimations
    }

    public static class TaeScriptInterpreter
    {
        public static bool MatchAnimPattern(string animIdStr, string pattern)
        {
            if (pattern == "*") return true;
            if (animIdStr.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                return true;

            try
            {
                string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
                return Regex.IsMatch(animIdStr, regexPattern, RegexOptions.IgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static (bool success, string output) RunScript(string scriptText, DSAProj proj, ExecutionScope scope, DSAProj.Animation currentAnim)
        {
            var logs = new List<string>();
            try
            {
                logs.Add("Tokenizing script...");
                var tokens = TaeScriptLexer.Lex(scriptText);

                logs.Add("Parsing and building AST...");
                var parser = new TaeScriptParser(tokens);
                var program = parser.ParseProgram();

                logs.Add("Executing script...");

                // Retrieve template dictionary
                var templates = proj.Template?.ToDictionary(kvp => (int)kvp.Key, kvp => kvp.Value) 
                                ?? new Dictionary<int, SoulsAssetPipeline.Animation.TAE.Template.ActionTemplate>();

                // Determine FPS
                float frameDuration = (float)TaeExtensionMethods.TAE_FRAME_30;
                if (zzz_DocumentManager.CurrentDocument?.EditorScreen?.Config?.ActionSnapType == TaeConfigFile.ActionSnapTypes.FPS60)
                {
                    frameDuration = (float)TaeExtensionMethods.TAE_FRAME_60;
                }

                var targetAnims = new List<DSAProj.Animation>();
                if (scope == ExecutionScope.CurrentAnimation)
                {
                    if (currentAnim != null)
                        targetAnims.Add(currentAnim);
                    else
                        throw new Exception("No current animation is selected.");
                }
                else if (scope == ExecutionScope.AllAnimations)
                {
                    foreach (var cat in proj.SAFE_GetAllAnimCategories())
                    {
                        targetAnims.AddRange(cat.SAFE_GetAnimations());
                    }
                }
                else // SelectedCategory
                {
                    var selectedCat = zzz_DocumentManager.CurrentDocument?.EditorScreen?.SelectedAnimCategory;
                    if (selectedCat != null)
                    {
                        targetAnims.AddRange(selectedCat.SAFE_GetAnimations());
                    }
                    else
                    {
                        throw new Exception("No animation category is selected.");
                    }
                }

                int matchedAnimCount = 0;
                int addedCount = 0;
                int removedCount = 0;
                int modifiedCount = 0;
                int shiftedCount = 0;

                foreach (var anim in targetAnims)
                {
                    string animIdStr = anim.SplitID.ToString();

                    foreach (var block in program.Blocks)
                    {
                        bool matched = false;
                        foreach (var pattern in block.Patterns)
                        {
                            if (MatchAnimPattern(animIdStr, pattern))
                            {
                                matched = true;
                                break;
                            }
                        }

                        if (matched)
                        {
                            matchedAnimCount++;
                            double maxTime = 1.0;
                            if (zzz_DocumentManager.CurrentDocument?.EditorScreen?.PlaybackCursor != null)
                            {
                                var cursor = zzz_DocumentManager.CurrentDocument.EditorScreen.PlaybackCursor;
                                if (cursor.HkxAnimationLength.HasValue)
                                {
                                    maxTime = cursor.HkxAnimationLength.Value;
                                }
                                else
                                {
                                    double maxEvTime = 0;
                                    foreach (var ev in anim.SAFE_GetActions())
                                    {
                                        if (ev.EndTime > maxEvTime)
                                            maxEvTime = ev.EndTime;
                                    }
                                    maxTime = maxEvTime > 0 ? maxEvTime : 1.0;
                                }
                            }

                            foreach (var stmt in block.Body)
                            {
                                stmt.Execute(anim, frameDuration, maxTime, templates, ref addedCount, ref removedCount, ref modifiedCount, ref shiftedCount, logs);
                            }
                        }
                    }
                }

                if (addedCount > 0 || removedCount > 0 || modifiedCount > 0 || shiftedCount > 0)
                {
                    Main.MainThreadLazyDispatch(() =>
                    {
                        if (zzz_DocumentManager.CurrentDocument?.EditorScreen?.Graph != null)
                        {
                            var activeAnim = zzz_DocumentManager.CurrentDocument.EditorScreen.SelectedAnim 
                                              ?? zzz_DocumentManager.CurrentDocument.EditorScreen.Graph.AnimRef;
                            if (activeAnim != null)
                            {
                                var selAnim = activeAnim.ParentProj.SAFE_SolveAnimRefChain(activeAnim.SplitID);
                                if (selAnim == activeAnim)
                                    selAnim = null;
                                zzz_DocumentManager.CurrentDocument.EditorScreen.Graph.ReadFromAnimRef(activeAnim, selAnim);
                            }
                        }
                    });
                }

                logs.Add("\nExecution completed successfully!");
                logs.Add($"Matched Animations: {matchedAnimCount}");
                logs.Add($"Added Events:       {addedCount}");
                logs.Add($"Removed Events:     {removedCount}");
                logs.Add($"Modified Events:    {modifiedCount}");
                logs.Add($"Shifted Events:     {shiftedCount}");

                return (true, string.Join("\n", logs));
            }
            catch (Exception ex)
            {
                logs.Add($"\n[Error] {ex.Message}");
                return (false, string.Join("\n", logs));
            }
        }
    }
}
