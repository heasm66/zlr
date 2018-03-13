namespace ZLR.Interfaces.SystemConsole.Debugger
{
    internal enum ValueType
    {
        Invalid,

        // rvalues
        Number,
        VariableNumber,
        Object,
        Attribute,
        Property,
        Pointer,
        PackedString,
        UnpackedString,
        Routine,
        ReadBuf,
        LexBuf,

        // lvalues
        Variable,
        ByteAtAddress,
        WordAtAddress,
    }
}