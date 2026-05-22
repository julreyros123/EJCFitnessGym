# Security Testing Guide
## Easy Step-by-Step Instructions

This guide shows you exactly how to test each security feature using free tools.

---

## 🛠️ Tools You Need (All Free)

### 1. **Browser Developer Tools** (Already Installed)
- **What:** Built into Chrome, Firefox, Edge
- **How to Open:** Press `F12` or `Ctrl+Shift+I`
- **Use For:** Inspecting cookies, network requests, HTML

### 2. **Postman** (Optional - for API testing)
- **Download:** https://www.postman.com/downloads/
- **Install:** Download and run installer
- **Use For:** Testing API endpoints

### 3. **OWASP ZAP** (Optional - for advanced testing)
- **Download:** https://www.zaproxy.org/download/
- **Install:** Download and run installer
- **Use For:** Automated vulnerability scanning

---

## 📋 Test Cases (Easy to Follow)

### Test 1: Password Hashing ✅

**What We're Testing:** Passwords are encrypted in database

**Steps:**
1. Open SQL Server Management Studio
2. Connect to your database
3. Run this query:
   ```sql
   SELECT TOP 5 Email, PasswordHash FROM AspNetUsers
   ```
4. **Expected Result:** PasswordHash column shows encrypted text like `AQAAAAIAAYag...`
5. **Screenshot:** Take screenshot of the query result

**✅ Pass Criteria:** Passwords are NOT readable plain text

---

### Test 2: Failed Login Attempts ✅

**What We're Testing:** System tracks failed login attempts

**Steps:**
1. Open your website in browser
2. Go to login page
3. Enter email: `test@example.com`
4. Enter wrong password: `wrongpassword`
5. Click Login
6. **Expected Result:** Error message "Invalid login attempt"
7. **Screenshot:** Take screenshot of error message

**✅ Pass Criteria:** Error message is shown, login is rejected

---

### Test 3: Account Lockout ✅

**What We're Testing:** Account locks after 5 failed attempts

**Steps:**
1. Go to login page
2. Enter email: `test@example.com`
3. Enter wrong password 5 times
4. Try to login 6th time
5. **Expected Result:** Message "This account has been locked out, please try again later"
6. **Screenshot:** Take screenshot of lockout message

**✅ Pass Criteria:** Account is locked after 5 attempts

---

### Test 4: Cookie Security ✅

**What We're Testing:** Authentication cookies are secure

**Steps:**
1. Login to your website
2. Press `F12` to open Developer Tools
3. Go to **Application** tab (Chrome) or **Storage** tab (Firefox)
4. Click **Cookies** → Select your website
5. Find `.AspNetCore.Identity.Application` cookie
6. Check these flags:
   - ✅ HttpOnly: Should be checked
   - ✅ Secure: Should be checked
   - ✅ SameSite: Should be "Lax"
7. **Screenshot:** Take screenshot showing cookie properties

**✅ Pass Criteria:** All three flags are set correctly

---

### Test 5: SQL Injection Protection ✅

**What We're Testing:** System blocks SQL injection attacks

**Steps:**
1. Go to login page
2. In email field, enter: `' OR '1'='1`
3. Enter any password
4. Click Login
5. **Expected Result:** Error "Invalid email format" or "Invalid login attempt"
6. **Screenshot:** Take screenshot of error

**✅ Pass Criteria:** Login is rejected, no SQL error shown

---

### Test 6: XSS Protection ✅

**What We're Testing:** System blocks JavaScript injection

**Steps:**
1. Login to your website
2. Go to Profile page
3. In First Name field, enter: `<script>alert('XSS')</script>`
4. Click Save
5. **Expected Result:** Script is saved as text, NOT executed
6. Press `F12` → Go to **Elements** tab
7. Find the First Name display
8. **Screenshot:** Should show `&lt;script&gt;` instead of `<script>`

**✅ Pass Criteria:** Script is encoded, not executed

---

### Test 7: Authorization Check ✅

**What We're Testing:** Members cannot access admin pages

**Steps:**
1. Login as a **Member** (not admin)
2. In browser address bar, type: `https://yourwebsite.com/Admin/Dashboard`
3. Press Enter
4. **Expected Result:** "Access Denied" page or redirect
5. **Screenshot:** Take screenshot of Access Denied page

**✅ Pass Criteria:** Member is blocked from admin page

---

### Test 8: CSRF Token ✅

**What We're Testing:** Forms have anti-CSRF protection

**Steps:**
1. Login to your website
2. Go to Profile page
3. Press `F12` → Go to **Elements** tab
4. Find the `<form>` tag
5. Look for hidden input: `<input name="__RequestVerificationToken" ...>`
6. **Screenshot:** Take screenshot showing the token

**✅ Pass Criteria:** Token is present in form

---

### Test 9: Weak Password Rejection ✅

**What We're Testing:** System rejects weak passwords

**Steps:**
1. Go to registration page
2. Enter email: `newuser@example.com`
3. Enter password: `password` (weak password)
4. Click Register
5. **Expected Result:** Error message about password requirements
6. **Screenshot:** Take screenshot of error message

**✅ Pass Criteria:** Weak password is rejected

---

### Test 10: Input Validation ✅

**What We're Testing:** System validates user input

**Steps:**
1. Login to your website
2. Go to Profile page
3. In Age field, enter: `150`
4. Click Save
5. **Expected Result:** Error "Age must be between 1 and 120"
6. **Screenshot:** Take screenshot of validation error

**✅ Pass Criteria:** Invalid age is rejected

---

## 📸 Screenshot Checklist

For your documentation, take screenshots of:

1. ✅ Database showing encrypted passwords
2. ✅ Failed login error message
3. ✅ Account lockout message
4. ✅ Cookie properties in DevTools
5. ✅ SQL injection blocked
6. ✅ XSS script encoded
7. ✅ Access Denied page
8. ✅ CSRF token in form
9. ✅ Weak password error
10. ✅ Validation error message

---

## 🔧 Using Postman (Optional)

### Test API Authentication

**Steps:**
1. Open Postman
2. Create new request
3. Set method to `GET`
4. Enter URL: `https://yourwebsite.com/api/finance/metrics`
5. Click Send
6. **Expected Result:** 401 Unauthorized
7. **Screenshot:** Take screenshot of 401 response

---

## 🔍 Using OWASP ZAP (Optional)

### Automated Security Scan

**Steps:**
1. Open OWASP ZAP
2. Click "Automated Scan"
3. Enter your website URL
4. Click "Attack"
5. Wait for scan to complete
6. Review results
7. **Screenshot:** Take screenshot of scan results

---

## 📊 Test Results Summary

Create a table like this:

| Test # | Test Name | Tool Used | Result | Screenshot |
|--------|-----------|-----------|--------|------------|
| 1 | Password Hashing | SQL Server | ✅ PASS | ✅ |
| 2 | Failed Login | Browser | ✅ PASS | ✅ |
| 3 | Account Lockout | Browser | ✅ PASS | ✅ |
| 4 | Cookie Security | DevTools | ✅ PASS | ✅ |
| 5 | SQL Injection | Browser | ✅ PASS | ✅ |
| 6 | XSS Protection | Browser | ✅ PASS | ✅ |
| 7 | Authorization | Browser | ✅ PASS | ✅ |
| 8 | CSRF Token | DevTools | ✅ PASS | ✅ |
| 9 | Weak Password | Browser | ✅ PASS | ✅ |
| 10 | Input Validation | Browser | ✅ PASS | ✅ |

**Total Tests:** 10  
**Passed:** 10  
**Failed:** 0  
**Success Rate:** 100%

---

## 💡 Tips for Screenshots

1. **Use Snipping Tool** (Windows) or **Screenshot** (Mac)
2. **Highlight important parts** with red boxes or arrows
3. **Include browser address bar** to show URL
4. **Make sure text is readable** - use high resolution
5. **Save with descriptive names** like `test1-password-hash.png`

---

## 🎯 Quick Testing (5 Minutes)

If you're short on time, do these 5 essential tests:

1. ✅ **Test 3:** Account Lockout (most important)
2. ✅ **Test 4:** Cookie Security
3. ✅ **Test 5:** SQL Injection
4. ✅ **Test 7:** Authorization
5. ✅ **Test 9:** Weak Password

---

## ❓ Troubleshooting

**Problem:** Can't see cookies in DevTools  
**Solution:** Make sure you're logged in first

**Problem:** SQL injection test shows different error  
**Solution:** That's okay! As long as login is rejected, it passes

**Problem:** Can't access admin page as member  
**Solution:** That's correct! It should be blocked

**Problem:** OWASP ZAP shows warnings  
**Solution:** Review each warning, some may be false positives

---

## 📝 For Your Documentation

Include these in your security documentation:

1. **List of tools used** (Browser DevTools, Postman, etc.)
2. **Test cases performed** (all 10 tests)
3. **Screenshots** (at least 10 screenshots)
4. **Test results table** (showing all passed)
5. **Date of testing** (when you performed tests)

---

## ✅ Completion Checklist

- [ ] All 10 tests performed
- [ ] All 10 screenshots taken
- [ ] Test results table created
- [ ] Screenshots added to documentation
- [ ] Tools list documented
- [ ] Date of testing recorded

---

**Testing Date:** _____________  
**Tested By:** _____________  
**Status:** ✅ All Tests Passed

