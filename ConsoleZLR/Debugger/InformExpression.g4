grammar InformExpression;

/*
 * Parser Rules
 */

compileUnit
	:	expression EOF
	;

primaryExpr
	:	Decimal_literal		# DecLiteral
	|	Hex_literal			# HexLiteral
	|	Binary_literal		# BinLiteral
	|   Char_literal		# CharLiteral
	|	Identifier			# Identifier
	|	Quoted_identifier	# QuotedIdentifier
	|	'(' expression ')'	# Parens
	;

memberExpr
	:	primaryExpr								# ToPrimaryExpr
	|	left=memberExpr '.' right=primaryExpr	# Member
	;

callExpr
	:	memberExpr						# ToMemberExpr
	|	left=callExpr '(' arguments ')'	# Call
	;

arguments
	:	( values+=assignmentExpr (',' values+=assignmentExpr)* )?
	;

memberAddressExpr
	:	callExpr										# ToCallExpr
	|	left=memberAddressExpr '.&' right=callExpr		# MemberAddress
	|	left=memberAddressExpr '.#' right=callExpr		# MemberLength
	;

postfixExpr
	:	memberAddressExpr		# ToMemberAddressExpr
	|	left=postfixExpr '++'	# PostIncrement
	|	left=postfixExpr '--'	# PostDecrement
	;

unaryExpr
	:	postfixExpr				# ToPostfixExpr
	|	'-' right=unaryExpr		# UnaryMinus
	;

dereferenceExpr
	:	unaryExpr										# ToUnaryExpr
	|	left=dereferenceExpr '->' right=unaryExpr		# DereferenceByte
	|	left=dereferenceExpr '-->' right=unaryExpr		# DereferenceWord
	;

multiplicativeExpr
	:	dereferenceExpr										# ToDereferenceExpr
	|	left=multiplicativeExpr '*' right=dereferenceExpr	# Multiplication
	|	left=multiplicativeExpr '/' right=dereferenceExpr	# Division
	|	left=multiplicativeExpr '%' right=dereferenceExpr	# Modulus
	|	left=multiplicativeExpr '&' right=dereferenceExpr	# BitwiseAnd
	|	left=multiplicativeExpr '|' right=dereferenceExpr	# BitwiseOr
	|	'~' right=multiplicativeExpr						# BitwiseNot
	;

additiveExpr
	:	multiplicativeExpr									# ToMultiplicativeExpr
	|	left=additiveExpr '+' right=multiplicativeExpr		# Addition
	|	left=additiveExpr '-' right=multiplicativeExpr		# Subtraction
	;

relationalExpr
	:	additiveExpr										# ToAdditiveExpr
	|	left=relationalExpr '==' right=orSequence			# Equality
	|	left=relationalExpr ('~=' | '!=') right=orSequence	# Inequality
	|	left=relationalExpr '>' right=additiveExpr			# Greater
	|	left=relationalExpr '>=' right=additiveExpr			# GreaterEqual
	|	left=relationalExpr '<' right=additiveExpr			# Less
	|	left=relationalExpr '<=' right=additiveExpr			# LessEqual
	|	left=relationalExpr 'has' right=additiveExpr		# Has
	|	left=relationalExpr 'hasnt' right=additiveExpr		# Hasnt
	|	left=relationalExpr 'in' right=additiveExpr			# In
	|	left=relationalExpr 'notin' right=additiveExpr		# Notin
	|	left=relationalExpr 'provides' right=additiveExpr	# Provides
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

expression
	:	assignmentExpr									# ToAssignmentExpr
	|	left=expression ',' right=assignmentExpr		# Comma
	;

/*
 * Lexer Rules
 */

WS
	:	(' ' | '\t' | '\r' | '\n') -> channel(HIDDEN)
	;

Decimal_literal
	:	Decimal_digit+
	;
fragment Decimal_digit
	:	[0-9]
	;

Hex_literal
	:	'$' Hex_digit+
	;
fragment Hex_digit
	:	[0-9a-fA-F]
	;

Binary_literal
	:	'$' '$' Binary_digit+
	;
fragment Binary_digit
	:	[01]
	;

Char_literal
	:	'\'' (Escaped_char | Non_escape_char) '\''
	;
fragment Escaped_char
	:	'\\' .
	;
fragment Non_escape_char
	:	~('\\' | '\'')
	;

Identifier
	:	Identifier_head Identifier_tail*
	;
fragment Identifier_head
	:	[a-zA-Z_?$]
	;
fragment Identifier_tail
	:	[a-zA-Z0-9_?$]
	;

Quoted_identifier
	:	'[' Quoted_identifier_char+ ']'
	;
fragment Quoted_identifier_char
	:	~(']' | '\\')
	|	Escaped_char
	;
