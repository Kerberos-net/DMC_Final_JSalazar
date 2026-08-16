# ADR 0014: Respaldo y continuidad

## Estado

Aceptado. Revisión 3. **El plan se diseña para producción y no se ejecuta en el entorno actual**, que
es una demostración académica sin contabilidad real de ninguna compañía. Las tres verificaciones que
la revisión 2 dejaba condicionando este ADR pasan de **bloqueantes** a **condiciones de puesta en
producción**.

La revisión 2 corrigió que la base es compartida con el sistema contable de la compañía —la revisión
1 la trataba como exclusiva del proyecto—, y cambió el enfoque de *"qué respaldo montamos"* a *"qué
añadimos al respaldo que ya existe"* (revisión adversarial v2, C7).

> **Qué significa esto en la práctica.** Nada de lo que sigue se implementa ni se ejecuta hoy. Este
> ADR existe porque el diseño tiene que decir cómo se protege el libro de compras de una empresa, y
> porque el día que el sistema opere con datos reales estas decisiones ya estarán tomadas y
> discutidas. **No es documentación de algo que funciona: es la condición para que pueda funcionar.**

Decisión nueva en la revisión 1: ni el diseño anterior ni ninguna de sus diez ADRs mencionaban
respaldo, frecuencia de copia, prueba de restauración, retención ni ubicación fuera del host.

## Contexto

El PRD elimina deliberadamente el sistema contable externo como destino: *"el asiento generado se
guarda únicamente en la base de datos asignada al software; no hay integración ni migración hacia
ningún sistema de gestión contable externo"*, y añade conservación indefinida.

Esa decisión convierte a **una instancia de SQL Server en el libro de compras de la empresa, sin
copia en ningún otro sistema**. Google Drive no cubre el hueco: contiene los adjuntos de las facturas
validadas, pero no los asientos, ni las correcciones, ni la auditoría, ni el estado de nada. Y ADR
0013 añade un segundo almacén: el volumen de documentos, que vive fuera de la base.

**El hecho que cambia esta revisión.** ADR 0003 estableció que las tablas maestras las mantiene el
sistema contable de la compañía **en esta misma base**. La base es compartida. Tres consecuencias que
la revisión 1 no consideraba:

1. **La cadena de log no se comparte.** Si el sistema contable ya toma sus propios `LOG BACKUP` a
   otro destino, las dos cadenas se intercalan y **ninguna de las dos restaura por sí sola**:
   recuperar exigiría ambos conjuntos, completos y en orden. Es un modo de fallo clásico de SQL
   Server y solo se descubre el día de la restauración.
2. **El modelo de recuperación no es una decisión de este proyecto.** `LOG BACKUP` exige modelo
   `FULL`. Si la base está en `SIMPLE`, cambiarla altera el crecimiento del log del sistema contable
   sin su consentimiento; si ya está en `FULL`, el punto 1 es casi seguro.
3. **La restauración no es local.** No se puede restaurar "las tablas de este proyecto". Restaurar la
   base a un punto en el tiempo **revierte también la contabilidad de la compañía**.

## Decisión

### Este proyecto no define la política de respaldo de la instancia

No fija el modelo de recuperación, no crea una cadena de `LOG BACKUP` propia y no cambia la
frecuencia del `FULL`. Esas decisiones pertenecen a quien administra la instancia, y tomarlas por
nuestra cuenta rompería el respaldo de un sistema ajeno.

**El RPO de 15 minutos deja de ser una decisión de este ADR y pasa a ser un requisito que se le
traslada al administrador.** Si la política vigente no lo alcanza, es una **restricción del
proyecto** que hay que declarar ante quien corresponda, no un respaldo que este proyecto monte por su
cuenta.

### El volumen de documentos sí es responsabilidad propia

| Frecuencia | Acción |
|---|---|
| Diario, paso 1 | Copia del **volumen de documentos** |
| Diario, paso 2 | Respaldo de la base, **según la política de la instancia** |

**El orden se conserva íntegro, y es la parte de la revisión 1 que sigue siendo enteramente nuestra.**
El volumen se copia **antes** que la base, de modo que toda fila que referencia un archivo tiene ese
archivo presente en la copia.

En el orden inverso, la base respaldada contendría referencias a documentos que la copia del volumen
todavía no capturó: **asientos contables sin su evidencia**. Es la mitigación concreta del costo que
ADR 0013 asume al elegir disco compartido.

Coordinar los dos pasos exige acordar la ventana con quien administra la instancia. Si el `FULL` de
la compañía corre a una hora fija, la copia del volumen debe terminar antes.

### El almacén de secretos

El respaldo del almacén de secretos (ADR 0015) es **responsabilidad propia y completa**: no vive en
la base compartida. Si se pierde, se pierde el acceso a Gmail, Drive, Sheets y Telegram.

### Restauración

Restaurar esta base **no es una operación de este proyecto**. Revierte también el sistema contable de
la compañía, de modo que el procedimiento es suyo y la decisión de ejecutarlo también.

Lo que este ADR sí decide: la **prueba** de restauración se ejecuta sobre una copia restaurada en
**entorno de prueba**, no en producción, y verifica que las tablas de este proyecto quedan íntegras y
consistentes con el volumen de documentos.

> Un respaldo que nunca se restauró no es un respaldo, es una suposición. La frase de la revisión 1
> sigue siendo cierta, y ahora apunta también a la prueba de restauración de la compañía, no solo a
> la nuestra.

## Condiciones de puesta en producción

Tres preguntas para quien administre la instancia **el día que el sistema opere con datos reales**.
Hoy no bloquean nada, porque no hay nada que perder; ese día bloquean la puesta en marcha:

| # | Pregunta | Por qué importa |
|---|---|---|
| 1 | ¿En qué **modelo de recuperación** está la base: `SIMPLE` o `FULL`? | `LOG BACKUP` exige `FULL`, y cambiarlo altera el crecimiento del log del sistema contable |
| 2 | ¿Existe ya una **cadena de `LOG BACKUP`**, a qué destino y con qué frecuencia? | Dos cadenas intercaladas **no restauran por separado** |
| 3 | ¿Cuál es el **RPO efectivo** hoy, y alcanza los 15 minutos que este proyecto necesita? | Si no los alcanza, es una restricción del proyecto que hay que declarar |

Ninguna se puede responder desde el diseño: dependen de cómo esté administrada una instancia que
este proyecto no controla.

## Alternativas consideradas

- **Base de datos propia para este proyecto**, con las tablas maestras leídas por vista, `synonym` o
  consulta entre bases. Volvería este ADR enteramente ejecutable, haría viables los permisos de ADR
  0003 sin negociación y quitaría la pregunta del derecho a DDL. **Se descartó por decisión del
  responsable**: todo se graba en la base asignada. Queda registrada porque es la salida si alguna
  de las tres premisas resulta bloqueante.
- **Imponer `FULL BACKUP` diario y `LOG BACKUP` cada 15 minutos sobre la base compartida**, que es lo
  que decía la revisión 1. Se descarta por los tres puntos del contexto: reasienta la base
  diferencial del sistema contable, puede exigir cambiarle el modelo de recuperación y produce dos
  cadenas de log que no restauran por separado.
- **Snapshot de la máquina completa.** Captura base y volumen en el mismo instante. Se descartó
  porque exige infraestructura de virtualización con soporte consistente para SQL Server, que no está
  decidida, y porque su RPO típico de 24 horas significa perder hasta un día de trabajo.
- **Respaldo directo a almacenamiento en la nube.** La copia sale del host por diseño. Se descartó
  como mecanismo primario porque introduce credenciales, costo recurrente y la necesidad de cifrar
  respaldos que son la contabilidad completa de la empresa saliendo de la red interna. Sigue siendo
  válido como destino secundario.
- **Confiar en Drive como copia.** Se descartó porque Drive solo tiene los adjuntos de las facturas
  ya validadas: ni asientos, ni auditoría, ni estado, ni los documentos previos a la validación.

## Consecuencias

- El respaldo de la base pasa de ser una fortaleza declarada del diseño a una **dependencia externa
  explícita**. Es menos cómodo de leer y más cierto.
- El orden entre los dos almacenes se conserva, y sigue siendo la decisión propia que más protege.
- **Costo:** coordinar la ventana de copia del volumen con la política de la instancia. Sin esa
  coordinación el orden deliberado no sirve de nada.
- **Costo:** la prueba de restauración necesita entorno de prueba y una copia restaurada aparte. Es
  más trabajo que restaurar en sitio, y es la única forma de probarlo sin tocar la contabilidad de la
  compañía.
- **Costo:** la retención frente a conservación indefinida crece de forma no acotada. Se dimensiona
  junto con el umbral de espacio libre de ADR 0015.
- **No cubre la disponibilidad.** Esto es recuperación ante pérdida, no continuidad de servicio. El
  worker sí tiene ahora vigilancia por latido (ADR 0015), pero su reinicio sigue siendo manual
  (ADR 0001).
- **El entorno actual no tiene respaldo, y es una decisión consciente.** Es una demostración
  académica: no hay contabilidad real que perder. Lo que sí hay que evitar es que esa ausencia se
  arrastre por inercia — de ahí que las tres condiciones de arriba estén escritas como bloqueo de
  puesta en producción y no como una nota al pie.
- **Riesgo declarado:** poner este sistema a registrar facturas reales sin haber respondido las tres
  preguntas significa sostener el libro de compras de una empresa sobre una base cuyo respaldo se
  desconoce. **No es un riesgo del diseño: es un riesgo de la decisión de arrancar.**
