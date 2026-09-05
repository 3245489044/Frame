using CodeRLFramework.VectorSpace;

namespace CodeRLFramework.Generation
{
    public class CodeTokenizer
    {
        public const int PAD_TOKEN = 0;
        public const int SOS_TOKEN = 1;
        public const int EOS_TOKEN = 2;
        public const int UNK_TOKEN = 3;

        public const int SPECIAL_TOKENS = 4;

        private readonly Dictionary<string, int> _functionToId;
        private readonly Dictionary<int, FunctionVector> _idToFunction;
        private readonly FunctionVectorSpace _functionSpace;

        public int VocabSize => SPECIAL_TOKENS + _functionToId.Count;

        public int FunctionCount => _functionToId.Count;

        public FunctionVectorSpace FunctionSpace => _functionSpace;

        public CodeTokenizer(FunctionVectorSpace functionSpace)
        {
            _functionSpace = functionSpace ?? throw new ArgumentNullException(nameof(functionSpace));
            _functionToId = new Dictionary<string, int>();
            _idToFunction = new Dictionary<int, FunctionVector>();

            int nextId = SPECIAL_TOKENS;
            foreach (var func in functionSpace.Functions)
            {
                if (!_functionToId.ContainsKey(func.Name))
                {
                    _functionToId[func.Name] = nextId;
                    _idToFunction[nextId] = func;
                    nextId++;
                }
            }
        }


        public int[] Encode(List<string> functionNames)
        {
            var tokens = new List<int> { SOS_TOKEN };

            foreach (var name in functionNames)
            {
                if (!string.IsNullOrEmpty(name) && _functionToId.TryGetValue(name, out int id))
                {
                    tokens.Add(id);
                }
                else
                {
                    tokens.Add(UNK_TOKEN);
                }
            }

            tokens.Add(EOS_TOKEN);
            return tokens.ToArray();
        }

        public int EncodeSingle(string functionName)
        {
            if (!string.IsNullOrEmpty(functionName) && _functionToId.TryGetValue(functionName, out int id))
                return id;
            return UNK_TOKEN;
        }


        public List<FunctionVector> Decode(int[] tokens)
        {
            var functions = new List<FunctionVector>();

            foreach (var token in tokens)
            {
                if (token >= SPECIAL_TOKENS && _idToFunction.TryGetValue(token, out var func))
                {
                    functions.Add(func);
                }
                if (token == EOS_TOKEN)
                    break;
            }

            return functions;
        }

        public FunctionVector? DecodeSingle(int token)
        {
            if (token >= SPECIAL_TOKENS && _idToFunction.TryGetValue(token, out var func))
                return func;
            return null;
        }

        public List<string> DecodeToNames(int[] tokens)
        {
            var names = new List<string>();

            foreach (var token in tokens)
            {
                if (token >= SPECIAL_TOKENS && _idToFunction.TryGetValue(token, out var func))
                {
                    names.Add(func.Name);
                }
                if (token == EOS_TOKEN)
                    break;
            }

            return names;
        }


        public int GetTokenId(string functionName)
        {
            return _functionToId.TryGetValue(functionName, out int id) ? id : UNK_TOKEN;
        }

        public string GetFunctionName(int tokenId)
        {
            return _idToFunction.TryGetValue(tokenId, out var func) ? func.Name : "<UNK>";
        }

        public static bool IsSpecialToken(int tokenId)
        {
            return tokenId < SPECIAL_TOKENS;
        }

        public bool IsFunctionToken(int tokenId)
        {
            return tokenId >= SPECIAL_TOKENS && _idToFunction.ContainsKey(tokenId);
        }

        public List<int> GetAllFunctionTokenIds()
        {
            return _idToFunction.Keys.OrderBy(k => k).ToList();
        }

        public string GetSummary()
        {
            return $"CodeTokenizer: VocabSize={VocabSize}, " +
                   $"Functions={FunctionCount}, " +
                   $"SpecialTokens={SPECIAL_TOKENS} " +
                   $"(PAD={PAD_TOKEN}, SOS={SOS_TOKEN}, EOS={EOS_TOKEN}, UNK={UNK_TOKEN})";
        }
    }
}
