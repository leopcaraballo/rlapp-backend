# ENGINEERING_PRINCIPLES.md

**RLAPP — Principios Fundamentales de Ingeniería de Software**

Este documento define los principios técnicos que gobiernan **todas las decisiones de diseño, implementación y evolución** del sistema RLAPP.

No son sugerencias.
Son **restricciones de ingeniería obligatorias** para mantener un sistema:

* Evolutivo
* Desacoplado
* Testeable
* Escalable
* Resiliente
* Arquitectónicamente estable en el tiempo

---

# 1. Principio de Primacía del Dominio

El **Dominio es el núcleo del sistema**.

Todo lo demás (infraestructura, frameworks, transporte, persistencia) es secundario.

## Reglas

* El dominio **no depende de nada externo**
* La lógica de negocio vive solo en el dominio
* Las invariantes se protegen dentro de los agregados
* El dominio expresa el lenguaje del negocio (Ubiquitous Language)

Si el dominio depende de infraestructura → el diseño es incorrecto.

---

# 2. Principio de Arquitectura Hexagonal

El sistema debe aislar completamente el núcleo del mundo externo.

## Regla de Dependencias

```
Domain ← Application ← Adapters ← Infrastructure
```

Nunca al revés.

## Consecuencia

* La infraestructura es reemplazable
* El dominio es testeable en aislamiento
* El sistema es evolutivo

---

# 3. Principio de Event Sourcing como Fuente de Verdad

El estado del sistema es una proyección de los eventos.

## Reglas

* Los eventos son inmutables
* Nunca se editan ni eliminan
* Representan hechos del dominio
* Permiten reconstrucción completa del estado
* Se versionan si evolucionan

El sistema **no guarda estado como fuente primaria**.

---

# 4. Principio de CQRS

Separación estricta entre:

* Escritura (modelo de dominio + eventos)
* Lectura (proyecciones optimizadas)

## Reglas

* Los Read Models no contienen lógica de negocio
* Pueden regenerarse desde eventos
* Pueden estar desnormalizados
* Son eventualmente consistentes

---

# 5. Principio SOLID Completo

El sistema debe cumplir los cinco principios:

### S — Single Responsibility

Cada componente tiene una única razón de cambio.

### O — Open/Closed

El sistema se extiende sin modificar código existente.

### L — Liskov Substitution

Las abstracciones deben ser sustituibles sin romper comportamiento.

### I — Interface Segregation

Interfaces pequeñas y específicas.

### D — Dependency Inversion

El núcleo depende de abstracciones, no de implementaciones.

---

# 6. Principio de Independencia de Infraestructura

La infraestructura es un detalle, no el diseño.

Debe poder reemplazarse sin modificar:

* Dominio
* Aplicación

Incluye:

* Base de datos
* Broker
* Cache
* Framework web
* Realtime gateway

Si cambiar infraestructura rompe negocio → fallo arquitectónico.

---

# 7. Principio de Consistencia por Eventos

El sistema es:

* Eventualmente consistente
* Determinístico
* Reprocesable

Debe soportar:

* Reintentos
* Reprocesamiento
* Eventos duplicados
* Recuperación tras fallos

---

# 8. Principio de Idempotencia

Todo componente debe tolerar ejecución múltiple sin corromper estado:

* Command handlers
* Event handlers
* Projections
* Consumers

La duplicación de eventos **no debe romper consistencia**.

---

# 9. Principio de Testabilidad Total

El dominio debe poder ejecutarse sin:

* Base de datos
* Docker
* HTTP
* Broker
* Infraestructura externa

Los tests deben:

* Validar invariantes
* Validar eventos
* Validar comportamiento
* Ser determinísticos

Si el dominio necesita infraestructura → el diseño viola DIP.

---

# 10. Principio de Invariantes Fuertes

Las reglas del negocio:

* Viven en el agregado
* No viven en Application
* No viven en Controllers
* No viven en DB
* No viven en servicios externos

Si una regla puede romperse → el modelo es incorrecto.

---

# 11. Principio de Efectos Secundarios Asíncronos

Todo efecto externo ocurre mediante eventos:

* Notificaciones
* Mensajería
* Integraciones
* Monitor en tiempo real

El dominio **no ejecuta efectos secundarios directos**.

---

# 12. Principio de Observabilidad

El sistema debe ser auditable y trazable:

* Logging estructurado
* Correlation IDs
* Trazabilidad por eventos
* Métricas operativas
* Auditoría completa

Sin contaminar el dominio.

---

# 13. Principio de Simplicidad Arquitectónica

Evitar:

* Overengineering
* Patrones innecesarios
* Abstracciones artificiales
* Complejidad accidental

La arquitectura protege el dominio, **no complica el sistema**.

---

# 14. Principio de Evolución Continua

El sistema debe permitir:

* Cambios sin romper historia
* Nuevos eventos
* Nuevas proyecciones
* Reprocesamiento completo
* Cambios de infraestructura
* Escalado horizontal

Sin reescribir el núcleo.

---

# 15. Principio de Integridad Arquitectónica

Si algo funciona pero rompe:

* Hexagonal
* Event Sourcing
* SOLID
* DIP
* Testabilidad

→ Está mal.

La arquitectura es un activo permanente.
El código es temporal.

---

# 16. Principio de Responsabilidad de Ingeniería

Cada ingeniero es responsable de:

* Proteger el dominio
* Evitar acoplamiento
* Mantener consistencia
* Escribir código testeable
* No degradar arquitectura
* Documentar decisiones
* Auditar el trabajo de la IA

---

# 17. La Regla Final

Prioridad absoluta:

1. Correctitud del dominio
2. Integridad arquitectónica
3. Consistencia por eventos
4. Testabilidad
5. Evolución sostenible
6. Performance

Nunca al revés.

---

**Estos principios gobiernan todas las decisiones técnicas en RLAPP.**
Si un cambio los viola, debe rechazarse o rediseñarse.

