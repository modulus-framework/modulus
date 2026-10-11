using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace Modulus.Identity.Scim;

/// <summary>The type of an attribute a filter may name.</summary>
internal enum ScimAttributeKind
{
    String,
    Boolean,
    Guid,
}

/// <summary>An attribute a filter may name and how to read it from the entity.</summary>
/// <typeparam name="T">The entity.</typeparam>
internal sealed record ScimAttribute<T>(ScimAttributeKind Kind, Expression<Func<T, object?>> Selector);

/// <summary>
/// Parses a SCIM filter (RFC 7644 section 3.4.2.2: <c>eq ne co sw ew pr</c>, <c>and</c>, <c>or</c>, <c>not</c>, parentheses)
/// into an expression the database can run. Only attributes the caller lists can be named; values travel as parameters,
/// never as text in a query. String comparison ignores case, as SCIM's <c>userName</c> does.
/// </summary>
internal static class ScimFilter
{
    private const string UserPrefix = "urn:ietf:params:scim:schemas:core:2.0:user:";
    private const string GroupPrefix = "urn:ietf:params:scim:schemas:core:2.0:group:";
    private const int MaxLength = 1000;
    private const int MaxDepth = 20;

    private static readonly MethodInfo s_toLower = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
    private static readonly MethodInfo s_contains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
    private static readonly MethodInfo s_startsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
    private static readonly MethodInfo s_endsWith = typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!;

    /// <summary>The predicate for <paramref name="filter"/>; null or blank gives null (no filtering).</summary>
    public static Expression<Func<T, bool>>? Parse<T>(string? filter, IReadOnlyDictionary<string, ScimAttribute<T>> attributes)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return null;
        if (filter.Length > MaxLength)
            throw Invalid("The filter is too long.");

        var parameter = Expression.Parameter(typeof(T), "e");
        var parser = new Parser<T>(Tokenize(filter), attributes, parameter);
        var body = parser.ParseAll();
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private static ScimException Invalid(string detail) => new(400, "invalidFilter", detail);

    private enum TokenKind
    {
        Word,
        Text,
        Open,
        Close,
    }

    private readonly record struct Token(TokenKind Kind, string Text);

    private static List<Token> Tokenize(string filter)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < filter.Length)
        {
            var c = filter[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '(')
            {
                tokens.Add(new Token(TokenKind.Open, "("));
                i++;
            }
            else if (c == ')')
            {
                tokens.Add(new Token(TokenKind.Close, ")"));
                i++;
            }
            else if (c == '"')
            {
                var text = new System.Text.StringBuilder();
                i++;
                var closed = false;
                while (i < filter.Length)
                {
                    if (filter[i] == '\\' && i + 1 < filter.Length)
                    {
                        text.Append(filter[i + 1]);
                        i += 2;
                    }
                    else if (filter[i] == '"')
                    {
                        closed = true;
                        i++;
                        break;
                    }
                    else
                    {
                        text.Append(filter[i++]);
                    }
                }

                if (!closed)
                    throw Invalid("A quoted value is not closed.");
                tokens.Add(new Token(TokenKind.Text, text.ToString()));
            }
            else
            {
                var start = i;
                while (i < filter.Length && !char.IsWhiteSpace(filter[i]) && filter[i] is not ('(' or ')' or '"'))
                    i++;
                tokens.Add(new Token(TokenKind.Word, filter[start..i]));
            }
        }

        return tokens;
    }

    private sealed class Box<TValue>(TValue value)
    {
        public TValue Value { get; } = value;
    }

    private sealed class Parser<T>(List<Token> tokens, IReadOnlyDictionary<string, ScimAttribute<T>> attributes, ParameterExpression parameter)
    {
        private int _position;
        private int _depth;

        public Expression ParseAll()
        {
            var result = ParseOr();
            if (_position != tokens.Count)
                throw Invalid("Unexpected text after the filter.");
            return result;
        }

        private Token? Peek() => _position < tokens.Count ? tokens[_position] : null;

        private bool PeekWord(string word)
            => Peek() is { Kind: TokenKind.Word } t && t.Text.Equals(word, StringComparison.OrdinalIgnoreCase);

        private Token Next()
            => _position < tokens.Count ? tokens[_position++] : throw Invalid("The filter ends too soon.");

        private Expression ParseOr()
        {
            var left = ParseAnd();
            while (PeekWord("or"))
            {
                _position++;
                left = Expression.OrElse(left, ParseAnd());
            }

            return left;
        }

        private Expression ParseAnd()
        {
            var left = ParseUnary();
            while (PeekWord("and"))
            {
                _position++;
                left = Expression.AndAlso(left, ParseUnary());
            }

            return left;
        }

        private Expression ParseUnary()
        {
            if (++_depth > MaxDepth)
                throw Invalid("The filter is nested too deeply.");
            try
            {
                if (PeekWord("not"))
                {
                    _position++;
                    return Expression.Not(ParseGroup());
                }

                if (Peek() is { Kind: TokenKind.Open })
                    return ParseGroup();

                return ParseComparison();
            }
            finally
            {
                _depth--;
            }
        }

        private Expression ParseGroup()
        {
            if (Next().Kind != TokenKind.Open)
                throw Invalid("Expected '('.");
            var inner = ParseOr();
            if (Next().Kind != TokenKind.Close)
                throw Invalid("Expected ')'.");
            return inner;
        }

        private Expression ParseComparison()
        {
            var pathToken = Next();
            if (pathToken.Kind != TokenKind.Word)
                throw Invalid("Expected an attribute name.");

            var path = pathToken.Text.ToLowerInvariant();
            if (path.StartsWith(UserPrefix, StringComparison.Ordinal))
                path = path[UserPrefix.Length..];
            else if (path.StartsWith(GroupPrefix, StringComparison.Ordinal))
                path = path[GroupPrefix.Length..];
            if (!attributes.TryGetValue(path, out var attribute))
                throw Invalid($"The filter cannot use the attribute '{pathToken.Text}'.");

            var typed = Expression.Convert(Replace(attribute.Selector, parameter), TypeOf(attribute.Kind));

            var opToken = Next();
            if (opToken.Kind != TokenKind.Word)
                throw Invalid("Expected an operator.");
            var op = opToken.Text.ToLowerInvariant();
            if (op == "pr")
                return Present(typed, attribute.Kind);

            var value = Next();
            return attribute.Kind switch
            {
                ScimAttributeKind.String => CompareString(typed, op, value),
                ScimAttributeKind.Boolean => BoolComparison(typed, op, value),
                _ => GuidComparison(typed, op, value),
            };
        }

        private static Type TypeOf(ScimAttributeKind kind) => kind switch
        {
            ScimAttributeKind.String => typeof(string),
            ScimAttributeKind.Boolean => typeof(bool),
            _ => typeof(Guid),
        };

        // Inlines the selector's body for the parameter, so EF sees plain member access.
        private Expression Replace(Expression<Func<T, object?>> selector, ParameterExpression target)
        {
            var body = selector.Body;
            if (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
                body = convert.Operand;
            return new ParameterReplacer(selector.Parameters[0], target).Visit(body);
        }

        private static Expression Present(Expression typed, ScimAttributeKind kind)
            => kind == ScimAttributeKind.String
                ? Expression.AndAlso(
                    Expression.NotEqual(typed, Expression.Constant(null, typeof(string))),
                    Expression.NotEqual(typed, Expression.Constant("", typeof(string))))
                : Expression.Constant(true);

        private static Expression Parameterized<TValue>(TValue value)
            => Expression.Property(Expression.Constant(new Box<TValue>(value)), nameof(Box<TValue>.Value));

        private static Expression CompareString(Expression member, string op, Token value)
        {
            if (value.Kind == TokenKind.Word && !value.Text.Equals("null", StringComparison.OrdinalIgnoreCase))
                throw Invalid("A text value must be quoted.");
            if (value.Kind != TokenKind.Text)
            {
                // eq null / ne null
                return op switch
                {
                    "eq" => Expression.Equal(member, Expression.Constant(null, typeof(string))),
                    "ne" => Expression.NotEqual(member, Expression.Constant(null, typeof(string))),
                    _ => throw Invalid($"The operator '{op}' needs a text value."),
                };
            }

            var needle = Parameterized(value.Text.ToLowerInvariant());
            var notNull = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
            var lowered = Expression.Call(member, s_toLower);
            Expression Guarded(Expression test) => Expression.AndAlso(notNull, test);

            return op switch
            {
                "eq" => Guarded(Expression.Equal(lowered, needle)),
                "ne" => Expression.Not(Guarded(Expression.Equal(lowered, needle))),
                "co" => Guarded(Expression.Call(lowered, s_contains, needle)),
                "sw" => Guarded(Expression.Call(lowered, s_startsWith, needle)),
                "ew" => Guarded(Expression.Call(lowered, s_endsWith, needle)),
                _ => throw Invalid($"The operator '{op}' is not supported for text."),
            };
        }

        private static Expression BoolComparison(Expression member, string op, Token value)
        {
            if (value.Kind != TokenKind.Word || !bool.TryParse(value.Text, out var flag))
                throw Invalid("Expected true or false.");
            return op switch
            {
                "eq" => Expression.Equal(member, Parameterized(flag)),
                "ne" => Expression.NotEqual(member, Parameterized(flag)),
                _ => throw Invalid($"The operator '{op}' is not supported for true/false."),
            };
        }

        private static Expression GuidComparison(Expression member, string op, Token value)
        {
            if (value.Kind != TokenKind.Text)
                throw Invalid("An id must be quoted.");
            // A malformed id matches nothing.
            var id = Guid.TryParse(value.Text, CultureInfo.InvariantCulture, out var parsed) ? parsed : Guid.Empty;
            var known = Guid.TryParse(value.Text, CultureInfo.InvariantCulture, out _);
            return op switch
            {
                "eq" => known ? Expression.Equal(member, Parameterized(id)) : Expression.Constant(false),
                "ne" => known ? Expression.NotEqual(member, Parameterized(id)) : Expression.Constant(true),
                _ => throw Invalid($"The operator '{op}' is not supported for ids."),
            };
        }
    }

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
