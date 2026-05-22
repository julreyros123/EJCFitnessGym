# Developer Tools Cheat Sheet
## Quick Reference for Security Testing

---

## ⌨️ Keyboard Shortcuts

### Open Developer Tools
| Action | Windows/Linux | Mac |
|--------|---------------|-----|
| Open DevTools | `F12` or `Ctrl+Shift+I` | `Cmd+Option+I` |
| Open Console | `Ctrl+Shift+J` | `Cmd+Option+J` |
| Inspect Element | `Ctrl+Shift+C` | `Cmd+Shift+C` |
| Close DevTools | `F12` or `Esc` | `Cmd+Option+I` |

### Navigate DevTools
| Action | Shortcut |
|--------|----------|
| Switch Tabs | `Ctrl+[` or `Ctrl+]` |
| Search in Files | `Ctrl+Shift+F` |
| Search in Page | `Ctrl+F` |
| Clear Console | `Ctrl+L` |
| Refresh Page | `Ctrl+R` or `F5` |
| Hard Refresh | `Ctrl+Shift+R` |

---

## 📑 DevTools Tabs Quick Reference

### 1. Elements / Inspector Tab
**What It Shows:** HTML structure of the page

**Use For:**
- ✅ Finding CSRF tokens
- ✅ Checking XSS encoding
- ✅ Inspecting form elements
- ✅ Viewing HTML structure

**Quick Actions:**
- `Ctrl+F` - Search HTML
- Right-click element → Edit as HTML
- Hover over element to highlight on page

---

### 2. Console Tab
**What It Shows:** JavaScript errors and logs

**Use For:**
- ✅ Checking for error messages
- ✅ Testing JavaScript
- ✅ Viewing console logs
- ✅ Running JavaScript commands

**Quick Actions:**
- `Ctrl+L` - Clear console
- Type JavaScript and press Enter to run
- `console.log()` to test code

**Useful Commands:**
```javascript
// Check if cookies are accessible
document.cookie

// Should return empty if HttpOnly is set
// If you see cookies, HttpOnly is NOT set ❌

// Test XSS
alert('test')

// Check current user
console.log(document.getElementById('user-info'))
```

---

### 3. Network Tab
**What It Shows:** All network requests

**Use For:**
- ✅ Checking HTTPS usage
- ✅ Viewing request/response headers
- ✅ Checking security headers
- ✅ Monitoring API calls

**Quick Actions:**
- `Ctrl+R` - Refresh to see requests
- Click request → Headers tab
- Filter by type (XHR, JS, CSS, etc.)
- Right-click → Copy as cURL

**What to Look For:**
```
✅ Protocol: h2 or https
✅ Status: 200, 301, 302 (not 500)
✅ Security Headers present
❌ Protocol: http (insecure)
❌ Passwords in URL
```

---

### 4. Application / Storage Tab
**What It Shows:** Cookies, storage, cache

**Use For:**
- ✅ Checking cookie security
- ✅ Viewing cookie properties
- ✅ Checking local storage
- ✅ Viewing session storage

**Quick Actions:**
- Expand Cookies → Select site
- Right-click cookie → Delete
- View cookie properties
- Clear all storage

**Cookie Properties to Check:**
```
✅ HttpOnly: ✓ (checked)
✅ Secure: ✓ (checked)
✅ SameSite: Lax or Strict
✅ Value: Encrypted (random text)
❌ HttpOnly: (empty)
❌ Secure: (empty)
❌ Value: Plain text
```

---

### 5. Sources / Debugger Tab
**What It Shows:** JavaScript source code

**Use For:**
- ✅ Viewing JavaScript files
- ✅ Setting breakpoints
- ✅ Debugging code
- ✅ Checking for exposed secrets

**Quick Actions:**
- `Ctrl+P` - Open file
- Click line number to set breakpoint
- `F8` - Resume execution
- `F10` - Step over

---

## 🎯 Security Testing Quick Guide

### Test 1: Cookie Security (2 minutes)
```
1. Press F12
2. Click "Application" tab
3. Expand "Cookies"
4. Click your site URL
5. Find .AspNetCore.Identity.Application
6. Check: HttpOnly ✓, Secure ✓, SameSite: Lax
7. Screenshot ✓
```

### Test 2: CSRF Token (2 minutes)
```
1. Press F12
2. Click "Elements" tab
3. Press Ctrl+F
4. Search: __RequestVerificationToken
5. Verify: type="hidden", value="[long string]"
6. Screenshot ✓
```

### Test 3: XSS Protection (3 minutes)
```
1. Enter: <script>alert('XSS')</script> in form
2. Save form
3. Press F12
4. Click "Elements" tab
5. Find where script is displayed
6. Verify: &lt;script&gt; (encoded)
7. Screenshot ✓
```

### Test 4: Network Security (2 minutes)
```
1. Press F12
2. Click "Network" tab
3. Press Ctrl+R to refresh
4. Check all requests show "https"
5. Click request → Headers
6. Verify security headers present
7. Screenshot ✓
```

### Test 5: Console Errors (1 minute)
```
1. Press F12
2. Click "Console" tab
3. Try to access forbidden page
4. Check no sensitive info in errors
5. Screenshot ✓
```

**Total Time: 10 minutes**

---

## 🔍 What to Look For

### ✅ GOOD Signs (Security is Working)

**Cookies:**
- HttpOnly flag is checked ✓
- Secure flag is checked ✓
- SameSite is "Lax" or "Strict"
- Value is encrypted (random text)

**Forms:**
- `__RequestVerificationToken` present
- Token is hidden (type="hidden")
- Token value is long random string

**HTML:**
- Scripts are encoded (`&lt;script&gt;`)
- No executable JavaScript in user input
- HTML tags are escaped

**Network:**
- All requests use HTTPS
- Security headers present
- No passwords in URLs

**Console:**
- Generic error messages only
- No stack traces
- No database details

---

### ❌ BAD Signs (Security Issues)

**Cookies:**
- HttpOnly is NOT checked ❌
- Secure is NOT checked ❌
- SameSite is "None" ❌
- Value is plain text ❌

**Forms:**
- No CSRF token ❌
- Token is visible to users ❌
- Token is empty ❌

**HTML:**
- Scripts are NOT encoded (`<script>`) ❌
- JavaScript executes from user input ❌
- Alert popups appear ❌

**Network:**
- Some requests use HTTP ❌
- Missing security headers ❌
- Passwords visible in URLs ❌

**Console:**
- Database errors visible ❌
- Stack traces shown ❌
- File paths exposed ❌

---

## 📸 Screenshot Checklist

### Cookie Security Screenshot Must Show:
- [ ] Application/Storage tab is open
- [ ] Cookies section is expanded
- [ ] Your website is selected
- [ ] Authentication cookie is visible
- [ ] HttpOnly column shows ✓
- [ ] Secure column shows ✓
- [ ] SameSite column shows "Lax"
- [ ] Value is encrypted (random text)

### CSRF Token Screenshot Must Show:
- [ ] Elements/Inspector tab is open
- [ ] Form element is expanded
- [ ] `__RequestVerificationToken` input is visible
- [ ] type="hidden" is visible
- [ ] value="[long string]" is visible

### XSS Protection Screenshot Must Show:
- [ ] Elements tab is open
- [ ] Script is encoded (`&lt;script&gt;`)
- [ ] Clear evidence of HTML encoding
- [ ] No executable script tags

### Network Security Screenshot Must Show:
- [ ] Network tab is open
- [ ] Multiple requests visible
- [ ] Protocol column shows "h2" or "https"
- [ ] OR Headers tab showing security headers

### Console Screenshot Must Show:
- [ ] Console tab is open
- [ ] Any error messages visible
- [ ] No sensitive information in errors
- [ ] Generic error messages only

---

## 💡 Pro Tips

### Tip 1: Use Element Picker
```
1. Click arrow icon (top-left of DevTools)
2. Hover over any element on page
3. Click to select it in Elements tab
4. Faster than searching manually!
```

### Tip 2: Preserve Network Log
```
1. Open Network tab
2. Check "Preserve log" checkbox
3. Requests won't disappear on page reload
```

### Tip 3: Filter Console Messages
```
1. Open Console tab
2. Click filter icon
3. Uncheck "Verbose" and "Info"
4. Only see important messages
```

### Tip 4: Search Across All Files
```
1. Press Ctrl+Shift+F
2. Search for: __RequestVerificationToken
3. Finds token in all files
```

### Tip 5: Copy Request as cURL
```
1. Open Network tab
2. Right-click any request
3. Copy → Copy as cURL
4. Paste in Postman or terminal
```

---

## 🆘 Troubleshooting

### Problem: DevTools Won't Open
**Solutions:**
- Try `Ctrl+Shift+I` instead of F12
- Try right-click → Inspect
- Check if DevTools is disabled by admin
- Restart browser

### Problem: Can't Find Application Tab
**Solutions:**
- In Firefox: Look for "Storage" tab
- In older browsers: Look for "Resources" tab
- Click >> icon to see more tabs

### Problem: Cookies Not Showing
**Solutions:**
- Make sure you're logged in
- Refresh the page
- Check if cookies are enabled
- Clear browser cache and try again

### Problem: Network Tab is Empty
**Solutions:**
- Refresh page (Ctrl+R)
- Check "Preserve log" checkbox
- Make sure you're on the right domain
- Clear and reload

### Problem: Too Many Console Messages
**Solutions:**
- Click "Clear console" (trash icon)
- Use filters to hide info/verbose
- Focus on errors and warnings only

---

## 📚 Browser Differences

### Chrome DevTools
- Tab: "Application" for cookies
- Tab: "Elements" for HTML
- Most features available

### Firefox DevTools
- Tab: "Storage" for cookies
- Tab: "Inspector" for HTML
- Similar to Chrome

### Edge DevTools
- Same as Chrome (Chromium-based)
- Tab: "Application" for cookies
- Tab: "Elements" for HTML

---

## ✅ Quick Testing Workflow

**Total Time: 10 minutes**

```
1. Open website → Login
2. Press F12
3. Test cookies (Application tab) → Screenshot
4. Test CSRF (Elements tab) → Screenshot
5. Test XSS (Elements tab) → Screenshot
6. Test network (Network tab) → Screenshot
7. Test console (Console tab) → Screenshot
8. Done! ✓
```

---

## 🎓 Learning Resources

**Chrome DevTools:**
- Official Docs: https://developer.chrome.com/docs/devtools/
- Video Tutorial: Search "Chrome DevTools tutorial" on YouTube

**Firefox DevTools:**
- Official Docs: https://firefox-source-docs.mozilla.org/devtools-user/
- Video Tutorial: Search "Firefox DevTools tutorial" on YouTube

**Security Testing:**
- OWASP Testing Guide: https://owasp.org/www-project-web-security-testing-guide/
- Web Security Academy: https://portswigger.net/web-security

---

**Last Updated:** May 2026  
**Difficulty:** ⭐ Easy  
**Time Required:** 10 minutes  
**Cost:** FREE (Built into browser)

