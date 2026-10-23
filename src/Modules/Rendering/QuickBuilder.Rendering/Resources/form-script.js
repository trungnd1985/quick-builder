(function(){var w=window,d=document;
function cp(v){return Array.from(v).length}
function nm(v,m){v=v.replace(/\r\n?/g,'\n');return m?v.replace(/\s+$/,''):v.replace(/\n/g,' ').trim()}
function em(v){if(v.length>254)return 0;for(var i=0;i<v.length;i++){var c=v.charCodeAt(i);if(c<33||c===127)return 0}var p=v.split('@');if(p.length!==2||!p[0])return 0;var t=p[1];for(i=1;i<t.length-1;i++)if(t[i]==='.')return 1;return 0}
function dt(v){if(!/^[0-9]{4}-[0-9]{2}-[0-9]{2}$/.test(v))return 0;var a=v.split('-').map(Number),x=new Date(Date.UTC(a[0],a[1]-1,a[2]));return x.getUTCFullYear()===a[0]&&x.getUTCMonth()===a[1]-1&&x.getUTCDate()===a[2]}
function ln(f,v){var n=cp(v);return f.minLength!=null&&n<f.minLength?'too_short':f.maxLength!=null&&n>f.maxLength?'too_long':null}
function one(f,v){switch(f.type){
case'email':return!em(v)?'email':ln(f,v);
case'phone':return!/^[0-9 +()-]+$/.test(v)||!/[0-9]/.test(v)?'phone':ln(f,v);
case'number':if(!/^-?[0-9]+([.][0-9]+)?$/.test(v))return'number';var n=Number(v);return f.whole&&!Number.isInteger(n)?'whole':f.min!=null&&n<f.min?'too_small':f.max!=null&&n>f.max?'too_large':null;
case'date':return!dt(v)?'date':f.earliest&&v<f.earliest?'too_early':f.latest&&v>f.latest?'too_late':null;
case'select':case'radio':return f.options.indexOf(v)<0?'choice':null;
default:return ln(f,v)}}
function check(f,s){if(f.type==='checkboxes'){var c=[];s.forEach(function(v){v=nm(v,0);if(v&&c.indexOf(v)<0)c.push(v)});if(!c.length)return{code:f.required?'required':null,values:[]};if(c.some(function(v){return f.options.indexOf(v)<0}))return{code:'choice',values:c};var o=f.options.filter(function(v){return c.indexOf(v)>=0});return{code:f.minSelected!=null&&o.length<f.minSelected?'too_few':f.maxSelected!=null&&o.length>f.maxSelected?'too_many':null,values:o}}
if(f.type==='checkbox'||f.type==='consent')return s.some(function(v){return v.trim()})?{code:null,values:['yes']}:{code:f.required?'required':null,values:[]};
var v=s.length?nm(s[0],f.type==='textarea'):'';return v.trim()?{code:one(f,v),values:[v]}:{code:f.required?'required':null,values:[]}}
if(w.__qbFormRulesHook)w.__qbFormRulesHook(check);
function num(e,a){var v=e&&e.getAttribute(a);return v==null||v===''?null:Number(v)}
function desc(b){var g=b.matches('fieldset'),k=b.classList.contains('qb-form__check'),x=b.querySelector('input:not([type=hidden]),select,textarea'),f={id:b.getAttribute('data-field'),label:b.getAttribute('data-label'),el:b,ctl:x,options:[]};
if(g){var r=b.querySelectorAll('input');f.type=r[0]&&r[0].type==='radio'?'radio':'checkboxes';r.forEach(function(i){f.options.push(i.value)});f.required=b.hasAttribute('data-required');f.minSelected=num(b,'data-min');f.maxSelected=num(b,'data-max')}
else if(k){f.type='checkbox';f.required=x.required}
else{var t=x.tagName==='TEXTAREA'?'textarea':x.tagName==='SELECT'?'select':{email:'email',tel:'phone',number:'number',date:'date'}[x.type]||'text';f.type=t;f.required=x.required;
if(t==='select')x.querySelectorAll('option').forEach(function(o){if(o.value)f.options.push(o.value)});
if(t==='number'){f.min=num(x,'min');f.max=num(x,'max');f.whole=x.getAttribute('step')==='1'}
else if(t==='date'){f.earliest=x.getAttribute('min');f.latest=x.getAttribute('max')}
else{f.minLength=num(x,'minlength');f.maxLength=num(x,'maxlength')}}return f}
function vals(fm,f){return new FormData(fm).getAll('f.'+f.id).map(String)}
function msg(fm,f,c){var own=f.ctl&&f.ctl.getAttribute('data-msg');if(own)return own;var m=fm.getAttribute('data-msg-'+c.replace(/_/g,'-'))||'',lo=f.type==='date'?f.earliest:f.type==='checkboxes'?f.minSelected:f.type==='number'?f.min:f.minLength,hi=f.type==='date'?f.latest:f.type==='checkboxes'?f.maxSelected:f.type==='number'?f.max:f.maxLength;return m.split('{label}').join(f.label).split('{min}').join(lo==null?'':lo).split('{max}').join(hi==null?'':hi)}
function el(t,c,x){var e=d.createElement(t);if(c)e.className=c;if(x)e.textContent=x;return e}
function clear(fm){fm.querySelectorAll('.qb-form__summary,.qb-form__banner,.qb-form__error').forEach(function(e){e.remove()});fm.querySelectorAll('[aria-invalid]').forEach(function(e){e.removeAttribute('aria-invalid')});fm.querySelectorAll('[aria-describedby]').forEach(function(e){var v=e.getAttribute('aria-describedby').split(' ').filter(function(i){return d.getElementById(i)}).join(' ');if(v)e.setAttribute('aria-describedby',v);else e.removeAttribute('aria-describedby')})}
function lead(fm,e){fm.insertBefore(e,fm.firstChild);e.tabIndex=-1;e.focus()}
function banner(fm,t){clear(fm);var b=el('div','qb-form__banner');b.setAttribute('role','alert');b.appendChild(el('p','',t));lead(fm,b)}
function show(fm,fs,errs){clear(fm);var s=el('div','qb-form__summary'),u=el('ul');s.setAttribute('role','alert');s.appendChild(el('h2','',fm.getAttribute('data-msg-summary')));s.appendChild(u);
fs.forEach(function(f){var c=errs[f.id];if(!c)return;var m=msg(fm,f,c),id=f.el.closest('.qb-form').id+'-e-'+f.id,p=el('p','qb-form__error',m);p.id=id;var a=f.el.querySelector('.qb-form__help[id]')||f.el.querySelector('label,legend');a.after(p);
var t=f.el.matches('fieldset')?f.el:f.ctl;t.setAttribute('aria-describedby',((t.getAttribute('aria-describedby')||'')+' '+id).trim());
f.el.querySelectorAll('input,select,textarea').forEach(function(i){i.setAttribute('aria-invalid','true')});var li=el('li'),l=el('a','',m);l.href='#'+(f.el.querySelector('input,select,textarea').id);li.appendChild(l);u.appendChild(li)});lead(fm,s)}
function sent(fm,to){if(to){w.location.assign(to);return}var r=fm.closest('.qb-form');r.classList.add('qb-form--sent');var p=r.querySelector('.qb-form__sent');p.focus()}
function init(fm){if(fm._qb)return;fm._qb=1;fm.noValidate=true;var fs=[].map.call(fm.querySelectorAll('[data-field]'),desc),btn=fm.querySelector('.qb-form__submit');
fm.addEventListener('submit',function(e){e.preventDefault();if(btn.disabled)return;var errs={},bad=0;fs.forEach(function(f){var r=check(f,vals(fm,f));if(r.code){errs[f.id]=r.code;bad=1}});if(bad){show(fm,fs,errs);return}
btn.disabled=true;fetch(fm.action,{method:'POST',headers:{Accept:'application/json'},body:new URLSearchParams(new FormData(fm))}).then(function(r){return r.json()}).then(function(j){btn.disabled=false;
if(j.status==='ok'){clear(fm);sent(fm,j.redirect)}else if(j.status==='invalid')show(fm,fs,j.errors||{});else banner(fm,fm.getAttribute(j.status==='limited'?'data-msg-limited':'data-msg-error'))}).catch(function(){btn.disabled=false;banner(fm,fm.getAttribute('data-msg-error'))})})}
function all(){d.querySelectorAll('form[data-qb-form]').forEach(init)}all();if(d.readyState==='loading')d.addEventListener('DOMContentLoaded',all)})();
