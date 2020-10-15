<VERSION XZIP>  
<CONSTANT RELEASEID 1>  
<GLOBAL READBUF <ITABLE BYTE 63>>  
<GLOBAL PARSEBUF <ITABLE BYTE 28>>  
<OBJECT PASSWORD (SYNONYM PASSWORD)>  
<ROUTINE GO () <CRLF> <TEST-READ>>  

<ROUTINE TEST-READ ()
    <PUTB ,READBUF 0 60>    ;"Max length of READBUF"  
    <PUTB ,PARSEBUF 0 0>    ;"Max # words that will be parsed (ZLR should not ignore this)"
    <TELL CR CR "You'll NEVER guess the password!!" CR>  
    <TELL "Enter password: ">  
            <DO (I 1 63) <PUTB ,READBUF .I 0>>  ;"Clear READBUF"  
            <DO (I 1 28) <PUTB ,PARSEBUF .I 0>> ;"Clear PARSEBUF"  
    <READ ,READBUF ,PARSEBUF>  
    <COND (<0? <GETB ,PARSEBUF 1>> <TELL "Nothing entered!" CR>)
          (<=? <GET ,PARSEBUF 1> ,W?PASSWORD> <TELL "Darn!!" CR>)
          (ELSE <TELL "Wrong password!" CR>)>
>  
