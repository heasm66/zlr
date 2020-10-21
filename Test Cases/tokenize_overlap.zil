<VERSION XZIP>  
<CONSTANT RELEASEID 1>  
<GLOBAL READBUF <ITABLE BYTE 63>>  
<GLOBAL PARSEBUF <ITABLE BYTE 28>>  
<ROUTINE GO () <CRLF> <TEST-READ>>  

<VOC "APPLE" OBJECT>
<VOC "BANANA" OBJECT>

<ROUTINE TEST-READ ("AUX" NW W P)
    <PUTB ,READBUF 0 60>    ;"Max length of READBUF"  
    <PUTB ,PARSEBUF 0 3>    ;"Max # words that will be parsed"
    <TELL "Enter words: ">  
            <DO (I 1 63) <PUTB ,READBUF .I 0>>  ;"Clear READBUF"  
            <DO (I 1 28) <PUTB ,PARSEBUF .I 0>> ;"Clear PARSEBUF"  
    <READ ,READBUF <>>
    <LEX ,READBUF ,PARSEBUF <> T>
    <SET NW <GETB ,PARSEBUF 1>> 
    <TELL N .NW " words parsed" CR>
    <SET P <REST ,PARSEBUF 2>>
    <DO (I 1 .NW)
    	<TELL "Word " N .I ": ">
    	<SET W <GET .P 0>>
    	<COND (.W <TELL !\" B .W !\">)
    	      (ELSE <TELL "nil">)>
    	<TELL " (start " N <GETB .P 3> ", len " N <GETB .P 2> ")">
    	<CRLF>
    	<SET P <+ .P 4>>>
>  
