namespace GeminiLab.Glug.Tokenizer {
    public enum GlugTokenTypeCategory {
        Literal     = 0x000,
        Symbol      = 0x001,
        Op          = 0x002,
        Keyword     = 0x003,
        Identifier  = 0x004,
    }

    // 12-bit category id + 12-bit S/N in category + 8-bit flags
    // flags:
    // bit 7 ... bit 0
    // | not used * 5 | a pseudo-token | with a integer | with a string |
    // pseudo token: won't be used by tokenizer, used in other place
    public enum GlugTokenType {
        LiteralNil      = 0x000_000_00,
        LiteralTrue     = 0x000_001_00,
        LiteralFalse    = 0x000_002_00,
        LiteralInteger  = 0x000_003_02,
        LiteralString   = 0x000_004_01,
        SymbolLParen    = 0x001_000_00,
        SymbolRParen    = 0x001_001_00,
        SymbolLBrace    = 0x001_002_00,
        SymbolRBrace    = 0x001_003_00,
        SymbolLBracket  = 0x001_004_00,
        SymbolRBracket  = 0x001_005_00,
        SymbolAssign    = 0x001_006_00,
        SymbolBackslash = 0x001_007_00,
        SymbolSemicolon = 0x001_008_00,
        SymbolComma     = 0x001_009_00,
        SymbolRArrow    = 0x001_00a_00,
        SymbolBang      = 0x001_00b_00,
        SymbolBangBang  = 0x001_00c_00,
        OpAdd           = 0x002_000_00,
        OpSub           = 0x002_001_00,
        OpMul           = 0x002_002_00,
        OpDiv           = 0x002_003_00,
        OpMod           = 0x002_004_00,
        OpLsh           = 0x002_005_00,
        OpRsh           = 0x002_006_00,
        OpAnd           = 0x002_007_00,
        OpOrr           = 0x002_008_00,
        OpXor           = 0x002_009_00,
        OpNot           = 0x002_00a_00,
        OpNeg           = 0x002_00b_04, // used in UnOp only
        OpGtr           = 0x002_00c_00,
        OpLss           = 0x002_00d_00,
        OpGeq           = 0x002_00e_00,
        OpLeq           = 0x002_00f_00,
        OpEqu           = 0x002_010_00,
        OpNeq           = 0x002_011_00,
        OpHash          = 0x002_012_00,
        OpCall          = 0x002_013_04,
        OpDollar        = 0x002_014_00,
        OpAt            = 0x002_015_00,
        // KeywordAt       = 0x003_000_00,
        KeywordIf       = 0x003_001_00,
        KeywordElif     = 0x003_002_00,
        KeywordElse     = 0x003_003_00,
        KeywordFn       = 0x003_004_00,
        KeywordReturn   = 0x003_005_00,
        KeywordWhile    = 0x003_006_00,
        Identifier      = 0x004_000_01,
        NotAToken       = 0x7ff_000_04,
    }
}