# Prueba de usabilidad y accesibilidad – Guía para el moderador

**Participantes:** 5 personas que no hayan usado FlashStore (compañeros de trabajo, familia, amigos).
**Duración:** 10 a 12 minutos por persona. **Dispositivo:** su propio teléfono (Chrome) o la laptop.
**URL:** `http://IP:3000`, la IP que imprime el pipeline 1. La red de la persona debe tener la IP autorizada en el firewall; lo más fácil es usar el Wi-Fi de la casa o la laptop.

## Antes de empezar (lo lee el moderador)

> "Vamos a probar una tienda en línea, no a usted. No hay respuestas incorrectas. Le voy a pedir 5 tareas; intente hacerlas sin ayuda y diga en voz alta lo que piensa. Si se traba, puede decir 'me rindo'."

Cuenta de prueba que se le entrega: `cliente2X@flashstore.test` / `Test1234!` (use cliente21 a cliente25, uno por participante).

## Tareas

| # | Tarea (se lee tal cual) | Éxito si… |
|---|---|---|
| T1 | "Busque unos audífonos y abra el producto." | Encuentra "Audífonos Bluetooth Wave" |
| T2 | "Inicie sesión con la cuenta que le di." | Ve "Mis pedidos" en el menú |
| T3 | "Compre los audífonos y pague con la tarjeta 4111 1111 1111 1111." | Ve "Pago aprobado" |
| T4 | "Revise en qué estado está su pedido." | Abre "Mis pedidos" y ve "Pagado" |
| T5 | "Compre otro producto y pague con la tarjeta 4000 0000 0000 0002. ¿Qué pasó?" | Explica que el pago fue rechazado |

Para cada tarea anote en `docs/usabilidad-resultados.xlsx`: **1 = completó / 0 = no**, el **tiempo en segundos** y el **número de errores** (clics equivocados, retrocesos).

## Cuestionario SUS (al final)

Se responde de 1 (totalmente en desacuerdo) a 5 (totalmente de acuerdo):

1. Creo que me gustaría usar esta tienda con frecuencia.
2. La tienda me pareció innecesariamente compleja.
3. Me pareció fácil de usar.
4. Creo que necesitaría ayuda de una persona técnica para usarla.
5. Las funciones de la tienda están bien integradas.
6. Hay demasiadas inconsistencias en la tienda.
7. Creo que la mayoría de personas aprendería a usarla muy rápido.
8. Me pareció muy complicada de usar.
9. Me sentí seguro(a) usando la tienda.
10. Tuve que aprender muchas cosas antes de poder usarla.

La hoja calcula el puntaje SUS de cada persona: `((suma impares − 5) + (25 − suma pares)) × 2.5`. **Meta: promedio ≥ 68** (por encima del promedio de la industria) y **tasa de éxito ≥ 80 %**.

## Accesibilidad con Lighthouse (10 minutos)

1. Chrome → abrir `http://IP:3000` → **F12** → pestaña **Lighthouse**.
2. **Mode:** Navigation · **Device:** Mobile · **Categories:** Accessibility, Best practices, Performance, SEO, PWA (si aparece).
3. **Analyze page load**. Captura del resumen de puntajes.
4. Repetir en `/login` y en `/carrito` (con sesión iniciada).
5. Guardar el informe: menú ⋮ del informe → **Save as HTML** → `reports/accessibility/lighthouse-catalogo.html`, etc.

**Meta:** accesibilidad ≥ 90. Anote cada problema que reporte Lighthouse (contraste, etiquetas, tamaño de botones) en la hoja "Lighthouse" de `usabilidad-resultados.xlsx`.
