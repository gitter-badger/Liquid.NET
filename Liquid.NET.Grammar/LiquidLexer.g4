lexer grammar LiquidLexer;
@lexer::header {#pragma warning disable 3021}

@lexer::members {
	public static readonly int WHITESPACE = 1;
 	int arraybracketcount = 0;

}

COMMENT :				COMMENTSTART ( COMMENT | . )*? COMMENTEND -> skip ; // TODO: skip
fragment COMMENTSTART:	TAGSTART [ \t]* 'comment' [ \t]* TAGEND ;
fragment COMMENTEND:	TAGSTART [ \t]* 'endcomment' [ \t]* TAGEND ;

RAW :					RAWSTART RAWBLOCK RAWEND ;
fragment RAWBLOCK:				( RAW | . )*?;
fragment RAWSTART:		TAGSTART [ \t]* 'raw' [ \t]* TAGEND ;
fragment RAWEND:		TAGSTART [ \t]* 'endraw' [ \t]* TAGEND ;

TAGSTART :				'{%'			-> pushMode(INLIQUIDTAG) ;
OUTPUTMKUPSTART :		'{{'			-> pushMode(INLIQUIDFILTER) ;
//TEXT :					.+? ;

TEXT :					.+? ;



mode NOMODE;

MINUS:					'-' ;

NUMBER :				MINUS? INT '.' [0-9]+ EXP? // 1.35, 1.35E-9, 0.3, -4.5
						| MINUS? INT EXP // 1e10 -3e4
						| MINUS? INT // -3, 45
						;

INT :			'0' | [1-9] [0-9]* ; // no leading zeros
fragment EXP :			[Ee] [+\-]? INT ; // \- since - means "range" inside [...]

BOOLEAN :				'true' | 'false' ;

ISEMPTY:				'empty?'; 
EMPTY :					'empty';
NULL :					'null'|'nil'; // Liquid uses both? (Note: this is also hardcoded in LiquidASTGenerator)
BLANK :					'blank';
ISBLANK:				'isblank?'; 

STRING :				'"' STRDOUBLE '"' | '\'' STRSINGLE '\'' ;  
fragment STRDOUBLE :	(ESC | ~["\\])* ;
fragment STRSINGLE :	(ESC | ~['\\])* ;
fragment ESC :			'\\' (["\\/bfnrt] | UNICODE) ;
fragment UNICODE :		'u' HEX HEX HEX HEX ;
fragment HEX :			[0-9a-fA-F] ;

LABEL :					ALPHA (ALPHA|DIGIT|UNDERSCORE)* ;

fragment UNDERSCORE:	'_' ;
fragment ALPHA:			[a-zA-Z] ;
fragment DIGIT:			[0-9] ;
 
FILTERPIPE :			'|' ;

PERIOD:					'.' ;

ARRAYSTART :			'[' ;
ARRAYEND :				']' ;

GENERATORSTART : 		'(';
GENERATOREND :			')';
GENERATORRANGE :		'..';

VARIABLETEST:			LABEL | NUMBER;

// SEE: http://stackoverflow.com/questions/18782388/antlr4-lexer-error-reporting-length-of-offending-characters#answer-18797779
ERRORCHAR :				. ;

// ========= COMMENT ===================

mode INCOMMENT;

NESTEDCOMMENT :			'{%' [ \t]+ 'comment' [ \t]+ '%}'  -> pushMode(INCOMMENT);
COMMENT_END:			'{%' [ \t]+ 'endcomment' [ \t]+ '%}' -> popMode ;

TEXT1 :					.+?  -> channel(HIDDEN);

// ========= LIQUID FILTERS ============

mode INLIQUIDFILTER ;

OUTPUTMKUPEND :			'}}'            -> popMode ;

FILTERPIPE1 :			FILTERPIPE -> type(FILTERPIPE) ;

PERIOD1:				PERIOD -> type(PERIOD) ;

NULL1:					NULL -> type(NULL);

EMPTY1:					EMPTY -> type(EMPTY);
ISEMPTY1:				ISEMPTY -> type(ISEMPTY);

BLANK1:					BLANK -> type(BLANK);
ISBLANK1:				ISBLANK -> type(ISBLANK);

NUMBER1:				NUMBER -> type(NUMBER);

BOOLEAN1:				BOOLEAN -> type(BOOLEAN);

STRING1:				STRING -> type(STRING);

LABEL1:					LABEL -> type(LABEL);

VARIABLETEST1:			VARIABLETEST -> type(VARIABLETEST);

ARRAYSTART1 :			'[' -> pushMode(INARRAYINDEX), type(ARRAYSTART) ;
ARRAYEND1 :				']' -> type(ARRAYEND);

COMMA :					',' ;
COLON :					':' ;

WS :					[ \t\r\n]+ -> skip ;

ERRORCHAR2 :			. -> type(ERRORCHAR);

mode INARRAYINDEX ;

ARRAYSTART2 :			ARRAYSTART {arraybracketcount++; System.Console.WriteLine("** Encountered nested '[' " +arraybracketcount);} -> type(ARRAYSTART);
ARRAYEND2a :				{arraybracketcount == 0; }? ARRAYEND {System.Console.WriteLine("** leaving mode " +arraybracketcount);} -> type(ARRAYEND), popMode ;
ARRAYEND2b :				{arraybracketcount > 0; }? ARRAYEND  { arraybracketcount--; System.Console.WriteLine("* closed nested ']' " +arraybracketcount); } -> type(ARRAYEND);
ARRAYINT:				'0' | MINUS ? [1-9] [0-9]* ;
STRING3:				STRING {System.Console.WriteLine("** Lexing a string " +arraybracketcount);}  -> type(STRING);
LABEL3:					LABEL -> type(LABEL) ;
MINUS3:					MINUS -> type(MINUS) ;

// ========= LIQUID TAGS ============

mode INLIQUIDTAG ;

TAGEND :				'%}'			-> popMode ;

INCLUDE_TAG :			'include' ;
WITH:					'with' ;
IF_TAG :				'if' ;
UNLESS_TAG :			'unless' ;
CASE_TAG :				'case' ;
WHEN_TAG :				'when' ;
ENDCASE_TAG :			'endcase' ;
ELSIF_TAG :				'elsif' ;
ELSE_TAG :				'else' ;
ENDIF_TAG :				'endif' ;
ENDUNLESS_TAG :			'endunless' ;
FOR_TAG :				'for' ;
FOR_IN :				'in';
BREAK_TAG :				'break';
CONTINUE_TAG :			'continue';
PARAM_REVERSED:			'reversed';
PARAM_OFFSET:		    'offset' ;
PARAM_LIMIT:		    'limit' ;
ENDFOR_TAG :			'endfor' ;
CYCLE_TAG :				'cycle' ;
ASSIGN_TAG :			'assign';
CAPTURE_TAG :			'capture';
ENDCAPTURE_TAG :		'endcapture';
INCREMENT_TAG :			'increment';
DECREMENT_TAG :			'decrement';
MACRO_TAG :				'macro' ;
ENDMACRO_TAG :			'endmacro' ;

ENDLABEL:				END LABEL;

NULL2:					NULL -> type(NULL);
EMPTY2:					EMPTY -> type(EMPTY);
ISEMPTY2:				ISEMPTY -> type(ISEMPTY);
BLANK2:					BLANK -> type(BLANK);
ISBLANK2:				ISBLANK -> type(ISBLANK);

COLON1 :				':' -> type(COLON);
COMMA1 :				',' -> type(COMMA);
ARRAYSTART3 :			'[' -> pushMode(INARRAYINDEX), type(ARRAYSTART) ;
ARRAYEND3 :				']' -> type(ARRAYEND);
ASSIGNEQUALS :			'=' ;
PARENOPEN :				'(' ;
PARENCLOSE :			')' ;
GT:						'>';
GTE:					'>=';
EQ:						'==';
NEQ:					'!=';
LT:						'<';
LTE:					'<=';
CONTAINS:				'contains';
AND:					'and';
OR:						'or';
MULT:					'*' ;
DIV:					'/' ;
MOD:					'%' ;
ADD:					'+' ;
MINUS2:					MINUS -> type(MINUS) ;

NOT :					'not' ;
NUMBER2 :				NUMBER -> type(NUMBER);
BOOLEAN2 :				BOOLEAN -> type(BOOLEAN);

FILTERPIPE2 :			FILTERPIPE -> type(FILTERPIPE) ;
COLON2 :				COLON -> type(COLON);
PERIOD2 :				PERIOD -> type(PERIOD) ;
STRING2:				STRING -> type(STRING);
LABEL2:					LABEL -> type(LABEL);
GENERATORRANGE1:		GENERATORRANGE -> type(GENERATORRANGE) ;

VARIABLETEST2:			VARIABLETEST -> type(VARIABLETEST);


END:					'end' ;

WS2 :					[ \t\r\n]+ -> skip ;

ERRORCHAR1 :			. -> type(ERRORCHAR);