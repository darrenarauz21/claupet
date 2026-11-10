---
id: calidad.pruebas
tipo: calidad
titulo: Pruebas
resumen: Cuánto probar según el riesgo del cambio y cómo dejar las pruebas
aplica: ["**/*"]
etiquetas: [pruebas, tests, riesgo, calidad]
prioridad: obligatoria
formato: 1
actualizado: 2026-11-10
---

## Reglas

- [segun-riesgo] El esfuerzo de pruebas va según el riesgo: un texto no necesita pruebas; auth, datos o dinero, sí y a fondo.
- [correr] Antes de dar algo por hecho se compila y se corren las pruebas que lo tocan.
- [falla-primero] Al corregir un error, primero una prueba que lo reproduzca, si es razonable.
- [decir-la-verdad] Si una prueba falla o se saltó, se dice con su salida; no se esconde.
- [sin-red] Las pruebas no dependen de internet ni del reloj real.
