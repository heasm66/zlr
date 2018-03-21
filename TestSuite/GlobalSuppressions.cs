
// This file is used by Code Analysis to maintain SuppressMessage 
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given 
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly:
    SuppressMessage("CodeContracts",
        "ContracsReSharperInterop_ContractForNotNull:Element with [NotNull] attribute does not have a corresponding not-null contract.",
        Justification = "Not using contracts")]
[assembly:
    SuppressMessage("CodeContracts",
        "ContracsReSharperInterop_CreateContractInvariantMethod:Missing Contract Invariant Method.",
        Justification = "Not using contracts")]

[assembly:
    SuppressMessage("CodeContracts",
        "ContracsReSharperInterop_CreateContractClass:Missing Contract Class.")]
