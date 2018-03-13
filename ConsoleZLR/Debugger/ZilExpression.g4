grammar ZilExpression;

/*
 * Parser Rules
 */

compileUnit
	:	expression EOF
	;

expression
	:	literalExpr			# Literal
	|	variableExpr		# Variable
	|	formExpr			# Form
	;

literalExpr
	:	Decimal_literal			# DecLiteral
	|	Octal_literal			# OctLiteral
	|	Binary_literal			# BinLiteral
	|   Char_literal			# CharLiteral
	|	'\''? identifier		# AtomLiteral
	|	'<' '>'					# FalseLiteral
	;

identifier
	:	Identifier
	|	keywordAsIdentifier
	;

variableExpr
	:	'.' expression		# Lval
	|	',' expression		# Gval
	;

formExpr
	:	arithmeticExpr
	|	bitwiseExpr
	|	comparisonExpr
	|	incrementExpr
	|	flagExpr
	|	logicalExpr
	|	propertyExpr
	|	setExpr
	|	tableExpr
	|	treeExpr
/*
	|	callExpr
*/
	;

setExpr
	:	'<' op=(K_SET | K_SETG) dest=expression value=expression '>'
	;

incrementExpr
	:	'<' op=(K_INC | K_DEC) expression '>'
	;

propertyExpr
	:	propertyReadExpr
	|	propertyWriteExpr
	|	propertyLenExpr
	;

propertyReadExpr
	:	'<' op=(K_GETP | K_GETPT) left=expression right=expression '>'
	;

propertyWriteExpr
	:	'<' op=K_PUTP left=expression right=expression value=expression '>'
	;

propertyLenExpr
	:	'<' K_PTLEN expression '>'
	;

tableExpr
	:	tableReadExpr
	|	tableWriteExpr
	;

tableReadExpr
	:	'<' op=(K_GET | K_GETB | K_GETsB) left=expression right=expression '>'
	;

tableWriteExpr
	:	'<' op=(K_PUT | K_PUTB | K_PUTsB) left=expression right=expression value=expression '>'
	;

treeExpr
	:	'<' K_INq left=expression right=expression '>'	# In
	|	'<' K_MOVE left=expression right=expression '>'	# Move
	|	'<' K_REMOVE left=expression '>'				# Remove
	|	'<' K_LOC expression '>'						# Parent
	|	'<' K_NEXTq expression '>'						# Sibling
	|	'<' K_FIRSTq expression '>'						# Child
	;

arithmeticExpr
	:	'<' '+' args+=expression* '>'					# Addition
	|	'<' '-' args+=expression* '>'					# Subtraction
	|	'<' '*' args+=expression* '>'					# Multiplication
	|	'<' '/' args+=expression* '>'					# Division
	|	'<' K_MOD left=expression right=expression '>'	# Modulus
	;

bitwiseExpr
	:	'<' K_BCOM expression '>'									# BitwiseNot
	|	'<' (K_BAND | K_ANDB) left=expression right=expression '>'	# BitwiseAnd
	|	'<' (K_BOR | K_ORB) left=expression right=expression '>'	# BitwiseOr
	;

comparisonExpr
	:	'<' (K_eq | K_EQUALq) left=expression rights+=expression+ '>'	# Equality
	|	'<' K_Neq left=expression rights+=expression+ '>'				# Inequality
	|	'<' op=K_Lq left=expression right=expression '>'				# Less
	|	'<' op=K_Leq left=expression right=expression '>'				# LessEqual
	|	'<' op=K_Gq left=expression right=expression '>'				# Greater
	|	'<' op=K_Geq left=expression right=expression '>'				# GreaterEqual
	;

logicalExpr
	:	'<' K_AND expression+ '>'	# LogicalAnd
	|	'<' K_OR expression+ '>'	# LogicalOr
	|	'<' K_NOT expression '>'	# LogicalNot
	;

flagExpr
	:	'<' K_FCLEAR left=expression right=expression '>'	# ClearFlag
	|	'<' K_FSET left=expression right=expression '>'		# SetFlag
	|	'<' K_FSETq left=expression right=expression '>'	# TestFlag
	;

/*
callExpr
	:	memberExpr						# ToMemberExpr
	|	left=callExpr '(' arguments ')'	# Call
	;

arguments
	:	( values+=assignmentExpr (',' values+=assignmentExpr)* )?
	;

orSequence
	:	alts+=additiveExpr ('or' alts+=additiveExpr)*
	;

booleanExpr
	:	relationalExpr									# ToRelationalExpr
	|	('~~' | '!') right=booleanExpr					# LogicalNot
	|	left=booleanExpr '&&' right=relationalExpr		# LogicalAnd
	|	left=booleanExpr '||' right=relationalExpr		# LogicalOr
	;

assignmentExpr
	:	booleanExpr										# ToBooleanExpr
	|	left=booleanExpr '=' right=assignmentExpr		# Assignment
	;
*/

/*
 * Lexer Rules
 */

WS
	:	(' ' | '\t' | '\r' | '\n') -> skip
	;

K_AND	:	[Aa][Nn][Dd];
K_ANDB	:	[Aa][Nn][Dd][Bb];
K_BAND	:	[Bb][Aa][Nn][Dd];
K_BCOM	:	[Bb][Cc][Oo][Mm];
K_BOR	:	[Bb][Oo][Rr];
K_DEC	:	[Dd][Ee][Cc];
K_eq	:	'='? '=?';
K_EQUALq:	[Ee][Qq][Uu][Aa][Ll] '?';
K_FCLEAR:	[Ff][Cc][Ll][Ee][Aa][Rr];
K_FIRSTq:	[Ff][Ii][Rr][Ss][Tt] '?';
K_FSET	:	[Ff][Ss][Ee][Tt];
K_FSETq	:	[Ff][Ss][Ee][Tt] '?';
K_GET	:	[Gg][Ee][Tt];
K_GETB	:	[Gg][Ee][Tt][Bb];
K_GETP	:	[Gg][Ee][Tt][Pp];
K_GETPT	:	[Gg][Ee][Tt][Pp][Tt];
K_GETsB	:	[Gg][Ee][Tt] '/' [Bb];
K_Gq	:	[Gg] '?';
K_Geq	:	[Gg] '=?';
K_INC	:	[Ii][Nn][Cc];
K_INq	:	[Ii][Nn] '?';
K_Lq	:	[Ll] '?';
K_Leq	:	[Ll] '=?';
K_LOC	:	[Ll][Oo][Cc];
K_MOD	:	[Mm][Oo][Dd];
K_MOVE	:	[Mm][Oo][Vv][Ee];
K_Neq	:	[Nn] '='? '=?';
K_NEXTq	:	[Nn][Ee][Xx][Tt] '?';
K_NOT	:	[Nn][Oo][Tt];
K_OR	:	[Oo][Rr];
K_ORB	:	[Oo][Rr][Bb];
K_PTLEN	:	[Pp][Tt][Ll][Ee][Nn];
K_PUT	:	[Pp][Uu][Tt];
K_PUTB	:	[Pp][Uu][Tt][Bb];
K_PUTP	:	[Pp][Uu][Tt][Pp];
K_PUTsB	:	[Pp][Uu][Tt] '/' [Bb];
K_REMOVE:	[Rr][Ee][Mm][Oo][Vv][Ee];
K_SET	:	[Ss][Ee][Tt];
K_SETG	:	[Ss][Ee][Tt][Gg];
K_T		:	[Tt];

keywordAsIdentifier
	:	K_AND
	|	K_ANDB
	|	K_BAND
	|	K_BCOM
	|	K_BOR
	|	K_DEC
	|	K_eq
	|	K_EQUALq
	|	K_FCLEAR
	|	K_FIRSTq
	|	K_FSET
	|	K_FSETq
	|	K_GET
	|	K_GETB
	|	K_GETP
	|	K_GETPT
	|	K_GETsB
	|	K_Gq
	|	K_Geq
	|	K_INC
	|	K_INq
	|	K_Lq
	|	K_Leq
	|	K_LOC
	|	K_MOD
	|	K_MOVE
	|	K_Neq
	|	K_NEXTq
	|	K_NOT
	|	K_OR
	|	K_ORB
	|	K_PTLEN
	|	K_PUT
	|	K_PUTB
	|	K_PUTP
	|	K_PUTsB
	|	K_REMOVE
	|	K_SET
	|	K_SETG
	|	K_T
	;

Decimal_literal
	:	[-+]? Decimal_digit+
	;
fragment Decimal_digit
	:	[0-9]
	;

Octal_literal
	:	'*' Octal_digit+ '*'
	;
fragment Octal_digit
	:	[0-7]
	;

Binary_literal
	:	'#2' WS+ Binary_digit+
	;
fragment Binary_digit
	:	[01]
	;

Char_literal
	:	'!\\' .
	;

fragment Escaped_char
	:	'\\' .
	;

Identifier
	:	Identifier_head Identifier_tail*
	;
fragment Identifier_head
	:	[a-zA-Z_0-9?$#+=/*]
	|	'-'
	|	Escaped_char
	;
fragment Identifier_tail
	:	'.'
	|	Identifier_head
	;
