using System;
using System.Diagnostics;

// ReSharper disable LocalizableElement

namespace ZLR.Interfaces.SystemConsole.Debugger
{
    internal struct Value
    {
        public readonly ValueType Type;
        public readonly int Content;

        private Value(ValueType type, int content)
        {
            Type = type;
            Content = content;
        }

        /// <summary>
        /// Indicates whether <see cref="Content"/> identifies the location where the value is stored, rather than the value itself.
        /// </summary>
        public bool IsReference => Type == ValueType.Variable || Type == ValueType.ByteAtAddress || Type == ValueType.WordAtAddress;

        /// <summary>
        /// Indicates whether <see cref="Content"/> contains the address of a location in Z-Machine memory.
        /// </summary>
        public bool IsUnpackedAddress => Type == ValueType.Pointer || Type == ValueType.UnpackedString ||
                                         Type == ValueType.ReadBuf || Type == ValueType.LexBuf ||
                                         Type == ValueType.ByteAtAddress || Type == ValueType.WordAtAddress;

        /// <summary>
        /// Indicates whether <see cref="Content"/> contains the address of a location in Z-Machine memory,
        /// which has been encoded by subtracting a base address (possibly 0) and dividing by a packing factor
        /// (possibly <see cref="Type"/>-specific).
        /// </summary>
        public bool IsPackedAddress => Type == ValueType.PackedString || Type == ValueType.Routine;

        /// <summary>
        /// Indicates whether <see cref="Content"/> contains the packed or unpacked address of a location in Z-Machine memory.
        /// </summary>
        public bool IsAddress => IsUnpackedAddress || IsPackedAddress;

        /// <summary>
        /// Indicates whether this is a meaningful value.
        /// </summary>
        public bool IsValid => Type != ValueType.Invalid;

        #region Arithmetic Operators

        public static Value operator +(Value left, Value right)
        {
            Debug.Assert(!left.IsReference);
            Debug.Assert(!right.IsReference);
            return Guard(left, right, (a, b) => new Value(AdditionResultType(a, b), a.Content + b.Content));
        }

        public static Value operator -(Value left, Value right)
        {
            Debug.Assert(!left.IsReference);
            Debug.Assert(!right.IsReference);
            return Guard(left, right, (a, b) => new Value(AdditionResultType(a, b), a.Content - b.Content));
        }

        private static ValueType AdditionResultType(Value left, Value right)
        {
            ValueType resultType;

            if (left.Type == ValueType.Number)
            {
                resultType = right.Type;
            }
            else if (right.Type == ValueType.Number)
            {
                resultType = left.Type;
            }
            else if (left.IsUnpackedAddress && !right.IsAddress || right.IsUnpackedAddress && !left.IsAddress)
            {
                resultType = ValueType.Pointer;
            }
            else
            {
                resultType = ValueType.Number;
            }

            return resultType;
        }

        public static Value operator *(Value left, Value right)
        {
            Debug.Assert(!left.IsReference);
            Debug.Assert(!right.IsReference);
            return Guard(left, right, (a, b) => new Value(ValueType.Number, a.Content * b.Content));
        }

        public static Value operator /(Value left, Value right)
        {
            Debug.Assert(!left.IsReference);
            Debug.Assert(!right.IsReference);
            return Guard(left, right, (a, b) => b.Content == 0 ? Invalid : new Value(ValueType.Number, a.Content / b.Content));
        }

        public static Value operator %(Value left, Value right)
        {
            Debug.Assert(!left.IsReference);
            Debug.Assert(!right.IsReference);
            return Guard(left, right, (a, b) => b.Content == 0 ? Invalid : new Value(ValueType.Number, a.Content % b.Content));
        }

        public static Value operator &(Value left, Value right)
        {
            Debug.Assert(!left.IsReference);
            Debug.Assert(!right.IsReference);
            return Guard(left, right, (a, b) => new Value(AdditionResultType(a, b), a.Content & b.Content));
        }

        public static Value operator |(Value left, Value right)
        {
            Debug.Assert(!left.IsReference);
            Debug.Assert(!right.IsReference);
            return Guard(left, right, (a, b) => new Value(AdditionResultType(a, b), a.Content | b.Content));
        }

        public static Value operator <(Value left, Value right)
        {
            Debug.Assert(!left.IsReference);
            Debug.Assert(!right.IsReference);
            return Guard(left, right, (a, b) => Boolean(a.Content < b.Content));
        }

        public static Value operator >(Value left, Value right)
        {
            Debug.Assert(!left.IsReference);
            Debug.Assert(!right.IsReference);
            return Guard(left, right, (a, b) => Boolean(a.Content > b.Content));
        }

        public static Value operator <=(Value left, Value right)
        {
            Debug.Assert(!left.IsReference);
            Debug.Assert(!right.IsReference);
            return Guard(left, right, (a, b) => Boolean(a.Content <= b.Content));
        }

        public static Value operator >=(Value left, Value right)
        {
            Debug.Assert(!left.IsReference);
            Debug.Assert(!right.IsReference);
            return Guard(left, right, (a, b) => Boolean(a.Content >= b.Content));
        }

        public static Value operator ~(Value right)
        {
            Debug.Assert(!right.IsReference);
            return Guard(right, a => Number(~a.Content));
        }

        public static Value operator !(Value right)
        {
            Debug.Assert(!right.IsReference);
            return Guard(right, a => Boolean(a.Content == 0));
        }

        public static Value operator -(Value right)
        {
            Debug.Assert(!right.IsReference);
            return Guard(right, a => Number(-a.Content));
        }

        public Value LogicalAnd(Value right)
        {
            Debug.Assert(!IsReference);
            Debug.Assert(!right.IsReference);
            return Guard(this, right, (a, b) => Boolean(a.Content != 0 && b.Content != 0));
        }

        public Value LogicalOr(Value right)
        {
            Debug.Assert(!IsReference);
            Debug.Assert(!right.IsReference);
            return Guard(this, right, (a, b) => Boolean(a.Content != 0 || b.Content != 0));
        }

        #endregion

        #region Type-Specific Factory Functions

        public static readonly Value Invalid = new Value(ValueType.Invalid, 0);

        public static Value Number(int num)
        {
            return new Value(ValueType.Number, num);
        }

        public static Value Boolean(bool b)
        {
            return new Value(ValueType.Number, b ? 1 : 0);
        }

        public static Value Object(int num)
        {
            return new Value(ValueType.Object, num);
        }

        public static Value Attribute(int num)
        {
            return new Value(ValueType.Attribute, num);
        }

        public static Value Property(int num)
        {
            return new Value(ValueType.Property, num);
        }

        public static Value Variable(int num)
        {
            return new Value(ValueType.Variable, num);
        }

        public static Value Routine(int num)
        {
            return new Value(ValueType.Routine, num);
        }

        public static Value Pointer(int num)
        {
            return new Value(ValueType.Pointer, num);
        }

        public static Value ByteAtAddress(int addr)
        {
            return new Value(ValueType.ByteAtAddress, addr);
        }

        public static Value WordAtAddress(int addr)
        {
            return new Value(ValueType.WordAtAddress, addr);
        }

        public static Value VariableNumber(int num)
        {
            return new Value(ValueType.VariableNumber, num);
        }

        #endregion

        public static Value Guard(Value a, Func<Value, Value> ifValid)
        {
            return a.IsValid ? ifValid(a) : Invalid;
        }

        public static Value Guard(Value a, Value b, Func<Value, Value, Value> ifValid)
        {
            return a.IsValid && b.IsValid ? ifValid(a, b) : Invalid;
        }

        public static Value Guard(Value a, Value b, Value c, Func<Value, Value, Value, Value> ifValid)
        {
            return a.IsValid && b.IsValid && c.IsValid ? ifValid(a, b, c) : Invalid;
        }
    }
}
