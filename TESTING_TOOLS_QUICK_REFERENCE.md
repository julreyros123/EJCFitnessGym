# Security Testing Tools - Quick Reference

## 🎯 Recommended Tools (Easiest to Use)

### 1. Browser Developer Tools ⭐ **START HERE**
**Cost:** FREE (Built-in)  
**Difficulty:** ⭐ Easy  
**Best For:** Cookie inspection, CSRF tokens, XSS testing

**How to Use:**
- Press `F12` in any browser
- Go to **Application** tab (Chrome) or **Storage** tab (Firefox)
- View cookies, local storage, session storage

**What You Can Test:**
- ✅ Cookie security (HttpOnly, Secure, SameSite)
- ✅ CSRF tokens in forms
- ✅ XSS protection (view encoded HTML)
- ✅ Network requests and responses

---

### 2. SQL Server Management Studio ⭐ **REQUIRED**
**Cost:** FREE  
**Difficulty:** ⭐ Easy  
**Best For:** Viewing encrypted passwords

**How to Use:**
1. Open SSMS
2. Connect to `(localdb)\mssqllocaldb`
3. Expand Databases → Your database
4. Run queries to view data

**What You Can Test:**
- ✅ Password hashing (view encrypted passwords)
- ✅ User roles (AspNetRoles table)
- ✅ Security audit logs

**Sample Queries:**
```sql
-- View encrypted passwords
SELECT TOP 5 Email, PasswordHash FROM AspNetUsers

-- View user roles
SELECT u.Email, r.Name as Role
FROM AspNetUsers u
JOIN AspNetUserRoles ur ON u.Id = ur.UserId
JOIN AspNetRoles r ON ur.RoleId = r.Id

-- View security audit logs
SELECT TOP 20 * FROM SecurityAuditLogs
ORDER BY EventTimestampUtc DESC
```

---

### 3. Postman ⭐ **RECOMMENDED**
**Cost:** FREE  
**Download:** https://www.postman.com/downloads/  
**Difficulty:** ⭐⭐ Medium  
**Best For:** API testing, authentication testing

**How to Use:**
1. Download and install Postman
2. Create new request
3. Set HTTP method (GET, POST, etc.)
4. Enter URL
5. Add headers/body if needed
6. Click Send

**What You Can Test:**
- ✅ API authentication (401 Unauthorized)
- ✅ API authorization (403 Forbidden)
- ✅ Rate limiting (429 Too Many Requests)
- ✅ CORS policies

**Example Tests:**
```
Test 1: Unauthorized API Access
- Method: GET
- URL: https://yoursite.com/api/finance/metrics
- Expected: 401 Unauthorized

Test 2: Brute Force Login
- Method: POST
- URL: https://yoursite.com/Identity/Account/Login
- Body: { "email": "test@test.com", "password": "wrong" }
- Repeat 5 times
- Expected: Account lockout
```

---

### 4. Browser Extensions (Optional)
**Cost:** FREE  
**Difficulty:** ⭐ Easy  
**Best For:** Quick security checks

**Recommended Extensions:**

**Cookie Editor**
- View and edit cookies easily
- Chrome: https://chrome.google.com/webstore (search "Cookie Editor")
- Firefox: https://addons.mozilla.org (search "Cookie Editor")

**Wappalyzer**
- Identify technologies used on website
- Shows: ASP.NET, Bootstrap, jQuery, etc.

**ModHeader**
- Modify HTTP request headers
- Test CORS, authentication headers

---

### 5. OWASP ZAP ⭐ **ADVANCED**
**Cost:** FREE  
**Download:** https://www.zaproxy.org/download/  
**Difficulty:** ⭐⭐⭐ Advanced  
**Best For:** Automated vulnerability scanning

**How to Use:**
1. Download and install OWASP ZAP
2. Click "Automated Scan"
3. Enter your website URL
4. Click "Attack"
5. Review results

**What You Can Test:**
- ✅ SQL Injection
- ✅ XSS vulnerabilities
- ✅ CSRF vulnerabilities
- ✅ Security headers
- ✅ SSL/TLS configuration

**Warning:** Only scan websites you own!

---

### 6. Burp Suite Community (Optional)
**Cost:** FREE (Community Edition)  
**Download:** https://portswigger.net/burp/communitydownload  
**Difficulty:** ⭐⭐⭐ Advanced  
**Best For:** Intercepting and modifying requests

**What You Can Test:**
- ✅ Session hijacking
- ✅ Authentication bypass
- ✅ Parameter tampering

---

## 📊 Tool Comparison

| Tool | Cost | Difficulty | Best For | Required? |
|------|------|------------|----------|-----------|
| Browser DevTools | FREE | ⭐ Easy | Cookies, CSRF, XSS | ✅ YES |
| SQL Server | FREE | ⭐ Easy | Password hashing | ✅ YES |
| Postman | FREE | ⭐⭐ Medium | API testing | ⭐ Recommended |
| OWASP ZAP | FREE | ⭐⭐⭐ Advanced | Auto scanning | Optional |
| Burp Suite | FREE | ⭐⭐⭐ Advanced | Request intercept | Optional |

---

## 🎯 Testing Strategy by Tool

### **Minimum Testing (Required)**
Use only built-in tools:
1. ✅ Browser DevTools - Cookie security, CSRF tokens
2. ✅ SQL Server - Password hashing
3. ✅ Browser - Manual testing (login, validation)

**Time:** 30 minutes  
**Screenshots:** 10

---

### **Standard Testing (Recommended)**
Add Postman:
1. ✅ Browser DevTools
2. ✅ SQL Server
3. ✅ Browser
4. ✅ Postman - API testing

**Time:** 1 hour  
**Screenshots:** 15

---

### **Advanced Testing (Optional)**
Add automated scanning:
1. ✅ Browser DevTools
2. ✅ SQL Server
3. ✅ Browser
4. ✅ Postman
5. ✅ OWASP ZAP - Automated scan

**Time:** 2 hours  
**Screenshots:** 20+

---

## 💡 Quick Start Guide

### For Beginners (No Extra Software)

**You Already Have:**
- ✅ Web Browser (Chrome, Firefox, Edge)
- ✅ SQL Server Management Studio
- ✅ Your website running locally

**What You Can Test:**
1. Password hashing (SQL Server)
2. Failed login attempts (Browser)
3. Account lockout (Browser)
4. Cookie security (Browser DevTools - F12)
5. SQL injection (Browser)
6. XSS protection (Browser)
7. Authorization (Browser)
8. CSRF tokens (Browser DevTools - F12)
9. Weak passwords (Browser)
10. Input validation (Browser)

**Result:** 10 complete tests with 0 additional software!

---

### For Intermediate Users

**Download:**
- ✅ Postman (5 minutes to install)

**Additional Tests:**
11. API authentication (Postman)
12. API authorization (Postman)
13. Rate limiting (Postman)

**Result:** 13 tests total

---

### For Advanced Users

**Download:**
- ✅ Postman
- ✅ OWASP ZAP (10 minutes to install)

**Additional Tests:**
14. Automated vulnerability scan (OWASP ZAP)
15. Security headers check (OWASP ZAP)
16. SSL/TLS configuration (OWASP ZAP)

**Result:** 16+ tests total

---

## 📸 Screenshot Guide by Tool

### Browser DevTools Screenshots:
1. Cookie properties (Application → Cookies)
2. CSRF token in HTML (Elements tab)
3. Network requests (Network tab)
4. Console errors (Console tab)

### SQL Server Screenshots:
1. Encrypted passwords query result
2. User roles query result
3. Security audit logs query result

### Postman Screenshots:
1. 401 Unauthorized response
2. 403 Forbidden response
3. Request/response headers

### OWASP ZAP Screenshots:
1. Scan summary
2. Vulnerability list
3. Risk assessment

---

## ✅ Testing Checklist

**Before You Start:**
- [ ] Website is running locally
- [ ] You have test accounts (member, admin)
- [ ] SQL Server is accessible
- [ ] Browser DevTools works (press F12)

**Required Tools Installed:**
- [x] Web Browser (already have)
- [x] SQL Server Management Studio (already have)
- [ ] Postman (optional but recommended)
- [ ] OWASP ZAP (optional for advanced)

**Ready to Test:**
- [ ] All tools installed
- [ ] Test accounts created
- [ ] Screenshot tool ready (Snipping Tool)
- [ ] Documentation template ready

---

## 🆘 Help & Support

**Problem:** Can't open DevTools  
**Solution:** Try `Ctrl+Shift+I` or right-click → Inspect

**Problem:** Can't connect to SQL Server  
**Solution:** Use connection string: `(localdb)\mssqllocaldb`

**Problem:** Postman not working  
**Solution:** Check if website is running first

**Problem:** OWASP ZAP shows errors  
**Solution:** Make sure website URL is correct

---

## 📚 Additional Resources

**Browser DevTools:**
- Chrome: https://developer.chrome.com/docs/devtools/
- Firefox: https://firefox-source-docs.mozilla.org/devtools-user/

**Postman:**
- Documentation: https://learning.postman.com/docs/
- Tutorials: https://www.youtube.com/c/Postman

**OWASP ZAP:**
- Documentation: https://www.zaproxy.org/docs/
- Getting Started: https://www.zaproxy.org/getting-started/

---

**Last Updated:** May 2026  
**Difficulty Ratings:** ⭐ Easy | ⭐⭐ Medium | ⭐⭐⭐ Advanced

