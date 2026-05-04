# Guía de Configuración de Email para RelativeNewsFilter

Esta guía te mostrará cómo configurar las alertas por email para el indicador RelativeNewsFilter.

---

## 📧 Gmail (Recomendado)

### Requisitos Previos
- Cuenta de Gmail activa
- Verificación en dos pasos habilitada

### Paso 1: Habilitar Verificación en Dos Pasos

1. Ve a https://myaccount.google.com/security
2. En "Cómo inicias sesión en Google", selecciona **Verificación en dos pasos**
3. Sigue las instrucciones para configurarlo (SMS o app Google Authenticator)

### Paso 2: Generar App Password

1. Ve a https://myaccount.google.com/apppasswords
2. Si no ves esta opción, asegúrate que la verificación en dos pasos esté activa
3. En "Seleccionar app", elige **Correo**
4. En "Seleccionar dispositivo", elige **Otro (nombre personalizado)**
5. Escribe "NinjaTrader" o "RelativeNewsFilter"
6. Click en **Generar**
7. **Copia el password de 16 caracteres** (formato: xxxx xxxx xxxx xxxx)
8. Guárdalo en un lugar seguro

### Paso 3: Configurar en NinjaTrader

1. Abre tu gráfico con el indicador RelativeNewsFilter
2. Click derecho en el gráfico → Indicators → RelativeNewsFilter
3. Ve a la pestaña **Email**
4. Configura:
   ```
   Enable Email Alerts: True
   Email Alert Minutes Before: 15
   SMTP Server: smtp.gmail.com
   SMTP Port: 587
   Email From: tu-email@gmail.com
   Email To: tu-email@gmail.com (o destinatario diferente)
   Email Password: xxxx xxxx xxxx xxxx (el App Password generado)
   ```
5. Click **OK**

### Paso 4: Probar

1. Espera a que se acerque un evento económico
2. Verifica tu bandeja de entrada
3. El email llegará con el asunto: `📰 NEWS ALERT: [Título] in [X] min`

---

## 📮 Outlook.com / Hotmail

### Configuración

```
SMTP Server: smtp-mail.outlook.com
SMTP Port: 587
Email From: tu-email@outlook.com
Email To: destinatario@email.com
Email Password: tu-contraseña-de-outlook
```

> **Nota**: Outlook.com también puede requerir un "App Password" dependiendo de tu configuración de seguridad. Si tienes 2FA habilitado, genera un App Password desde la configuración de seguridad de tu cuenta.

---

## 📨 Yahoo Mail

### Configuración

```
SMTP Server: smtp.mail.yahoo.com
SMTP Port: 587
Email From: tu-email@yahoo.com
Email To: destinatario@email.com
Email Password: [App Password]
```

### Generar App Password en Yahoo

1. Ve a https://login.yahoo.com/myaccount/security
2. Click en **Generate app password**
3. Selecciona "Other App" y escribe "NinjaTrader"
4. Usa ese password en la configuración

---

## 🔐 Otros Proveedores SMTP

Si usas otro proveedor (empresarial, etc.), necesitarás:

1. **Servidor SMTP** - Consulta con tu proveedor (ej: mail.tudominio.com)
2. **Puerto** - Usualmente 587 (TLS) o 465 (SSL)
3. **Credenciales** - Tu email y contraseña

### Ejemplo Genérico
```
SMTP Server: smtp.tuproveedor.com
SMTP Port: 587
Email From: tu@empresa.com
Email To: destinatario@email.com
Email Password: tu-contraseña
```

---

## 🧪 Solución de Problemas

### "Email error: The SMTP server requires a secure connection"
- Verifica que el puerto sea 587 (no 25 o 465)
- Gmail y la mayoría de proveedores usan TLS en puerto 587

### "Email error: Authentication failed"
- Gmail: Asegúrate de usar **App Password**, NO tu contraseña normal
- Verifica que no haya espacios extra en email o password
- Confirma que el Email From sea correcto

### "Email error: Unable to connect to the remote server"
- Verifica tu conexión a internet
- Confirma que el servidor SMTP sea correcto
- Algunos firewalls bloquean puerto 587 - contacta IT si estás en red corporativa

### No Recibo Emails
- Revisa carpeta de SPAM/Correo no deseado
- Verifica que `Enable Email Alerts` esté en `true`
- Confirma que `Email Alert Minutes Before` tenga un valor razonable (15-30)
- Los emails solo se envían en modo **Realtime**, no en Historical/Playback
- Revisa Output Window de NinjaTrader para mensajes de error

### Recibo Múltiples Emails del Mismo Evento
- Esto NO debería pasar (hay sistema anti-duplicados)
- Si ocurre, reporta el bug con detalles del evento

---

## 📬 Formato del Email

Los emails tienen este formato:

```
Asunto: 📰 NEWS ALERT: Fed Interest Rate Decision in 12 min

Cuerpo:
=== NEWS ALERT ===
Event: Fed Interest Rate Decision
Country: USD
Impact: High
Time: 2026-01-19 14:00
Minutes Until: 12
==================
Avoid trading during this period.
```

---

## ⚡ Mejores Prácticas

1. **Usa Email Alert Minutes Before = 15-30 min** - Tiempo suficiente para reaccionar
2. **Prueba primero con FilterImpact = "High"** - Solo eventos críticos
3. **Verifica spam la primera vez** - Marca como "No es spam" si es necesario
4. **No compartas tu App Password** - Es equivalente a tu contraseña
5. **Revoca passwords antiguos** - Si cambias de PC, genera uno nuevo

---

## 🔄 Cambiar Configuración

Para cambiar la configuración de email:

1. Click derecho en el gráfico
2. Indicators → RelativeNewsFilter
3. Modifica parámetros en pestaña Email
4. Click OK
5. El indicador se recargará con la nueva configuración

---

## 📊 Estadísticas de Uso

- Los emails se envían SOLO en modo Realtime
- Un email por evento (anti-duplicados)
- Solo eventos que coinciden con:
  - Tu instrumento (auto-detección de moneda)
  - FilterImpact configurado
  - Dentro de EmailAlertMinutes

---

## ❓ FAQ

**¿Puedo enviar emails a múltiples destinatarios?**  
No directamente. Pero puedes:
- Poner varios emails separados por coma en `Email To` (algunos servidores lo permiten)
- Usar un alias/grupo en Gmail
- Configurar reenvío automático en Gmail

**¿Los emails funcionan en Playback?**  
No, solo en modo Realtime. Es intencional para evitar spam durante pruebas.

**¿Cuánto cuesta?**  
Los emails son gratuitos usando tu cuenta personal (Gmail, Outlook, etc.)

**¿Hay límite de emails?**  
Gmail tiene límites diarios (~500 emails/día), pero con los eventos económicos nunca lo alcanzarás.

---

## 📞 Soporte

Si sigues teniendo problemas, contacta al desarrollador con:
- Configuración completa (sin incluir password)
- Mensaje de error exacto del Output Window
- Proveedor de email que usas
