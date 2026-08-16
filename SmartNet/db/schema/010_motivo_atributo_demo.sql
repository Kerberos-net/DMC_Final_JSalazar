-- 010_motivo_atributo_demo.sql
-- Reclasificacion de motivos de demo (Work Unit 4, Phase 4) -- MOTIVOS-CLASIFICACION.md, "Decision
-- de demo, no contable". Los 23 motivos marcados con `†` en su tabla completa se reclasifican de
-- `07 CAJA CHICA` a `02 COMPRAS` UNICAMENTE para que la demostracion los muestre en el registro de
-- compras; no es la clasificacion contable real, y el propio documento pide revertirla antes de
-- produccion.
--
-- La cuenta de 23 se remidio directamente sobre MOTIVOS-CLASIFICACION.md en esta sesion (apply,
-- Work Unit 4): 23 filas marcadas con `†` en la tabla completa, ninguna mas, ninguna menos --
-- coincide con spec.md y con tasks.md. Ver el resumen final para donde y como se conto.
--
-- `INSERT ... SELECT` desde `dbo.Motivo`, emparejado por el numero de motivo (su propia clave, un
-- INT), nunca contra un id inventado -- este proyecto no conoce ni debe inventar las filas de
-- dbo.Motivo (SELECT unicamente, ADR 0003 clase "externa"; ver DboWriteLintTests.cs, que permite
-- exactamente esta lectura y sigue prohibiendo cualquier escritura a dbo).
--
-- Insert-if-absent (design.md): un solo motivo representativo (5) decide si el bloque ya se aplico;
-- los 23 se insertan siempre juntos, en una sola sentencia, asi que comprobar uno solo basta. Esto
-- es lo que hace que reaplicar el script converja en vez de fallar o duplicar.
IF NOT EXISTS (SELECT 1 FROM fact.MotivoAtributo WHERE Motivo = 5)
BEGIN
    INSERT INTO fact.MotivoAtributo (Motivo, OrigenLibro, Activo)
    SELECT codigo, '02', 1
    FROM dbo.Motivo
    WHERE codigo IN (5, 13, 16, 17, 18, 19, 20, 21, 30, 38, 40, 42, 46, 48, 49, 53, 56, 59, 60, 77, 81, 88, 90);

    -- La guarda existe precisamente para que un conteo equivocado falle en voz alta al aplicar el
    -- esquema, en vez de sembrar un libro incorrecto en silencio (spec.md). Dispara si dbo.Motivo
    -- no trae, en ese momento, exactamente esos 23 codigos -- en el entorno real porque el catalogo
    -- cambio, o en pruebas porque el fixture de la base de pruebas no los sembro todos.
    IF @@ROWCOUNT <> 23
        THROW 50002, 'Se esperaban exactamente 23 motivos reclasificados desde dbo.Motivo (MOTIVOS-CLASIFICACION.md); el conteo real fue distinto.', 1;
END
