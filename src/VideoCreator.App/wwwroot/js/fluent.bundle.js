function i(t,e,o,r){var n=arguments.length,a=n<3?e:r===null?r=Object.getOwnPropertyDescriptor(e,o):r,s;if(typeof Reflect=="object"&&typeof Reflect.decorate=="function")a=Reflect.decorate(t,e,o,r);else for(var d=t.length-1;d>=0;d--)(s=t[d])&&(a=(n<3?s(a):n>3?s(e,o,a):s(e,o))||a);return n>3&&a&&Object.defineProperty(e,o,a),a}var bt=class{constructor(e,o,r=!1){this.evaluate=e,this.policy=o,this.isVolatile=r}};var q;(function(t){t[t.needsArrayObservation=1101]="needsArrayObservation",t[t.onlySetDOMPolicyOnce=1201]="onlySetDOMPolicyOnce",t[t.bindingInnerHTMLRequiresTrustedTypes=1202]="bindingInnerHTMLRequiresTrustedTypes",t[t.twoWayBindingRequiresObservables=1203]="twoWayBindingRequiresObservables",t[t.hostBindingWithoutHost=1204]="hostBindingWithoutHost",t[t.unsupportedBindingBehavior=1205]="unsupportedBindingBehavior",t[t.directCallToHTMLTagNotAllowed=1206]="directCallToHTMLTagNotAllowed",t[t.onlySetTemplatePolicyOnce=1207]="onlySetTemplatePolicyOnce",t[t.cannotSetTemplatePolicyAfterCompilation=1208]="cannotSetTemplatePolicyAfterCompilation",t[t.blockedByDOMPolicy=1209]="blockedByDOMPolicy",t[t.invalidHydrationAttributeMarker=1210]="invalidHydrationAttributeMarker",t[t.duplicateRenderInstruction=1211]="duplicateRenderInstruction",t[t.missingElementDefinition=1401]="missingElementDefinition",t[t.noRegistrationForContext=1501]="noRegistrationForContext",t[t.noFactoryForResolver=1502]="noFactoryForResolver",t[t.invalidResolverStrategy=1503]="invalidResolverStrategy",t[t.cannotAutoregisterDependency=1504]="cannotAutoregisterDependency",t[t.cannotResolveKey=1505]="cannotResolveKey",t[t.cannotConstructNativeFunction=1506]="cannotConstructNativeFunction",t[t.cannotJITRegisterNonConstructor=1507]="cannotJITRegisterNonConstructor",t[t.cannotJITRegisterIntrinsic=1508]="cannotJITRegisterIntrinsic",t[t.cannotJITRegisterInterface=1509]="cannotJITRegisterInterface",t[t.invalidResolver=1510]="invalidResolver",t[t.invalidKey=1511]="invalidKey",t[t.noDefaultResolver=1512]="noDefaultResolver",t[t.cyclicDependency=1513]="cyclicDependency",t[t.connectUpdateRequiresController=1514]="connectUpdateRequiresController"})(q||(q={}));var Ge=t=>typeof t=="function",me=t=>typeof t=="string",sa=()=>{};var Ld=Object.create(null),Q={warn(t,e){},error(t,e){return new Error(`Error ${t}`)},addMessages(t){Object.assign(Ld,t)}};var la=Object.freeze([]);function _t(){let t=new Map;return Object.freeze({register(e){return t.has(e.type)?!1:(t.set(e.type,e),!0)},getByType(e){return t.get(e)},getForInstance(e){if(e!=null)return t.get(e.constructor)}})}function rr(){let t=new WeakMap;return function(e){let o=t.get(e);if(o===void 0){let r=Reflect.getPrototypeOf(e);for(;o===void 0&&r!==null;)o=t.get(r),r=Reflect.getPrototypeOf(r);o=o===void 0?[]:o.slice(0),t.set(e,o)}return o}}function be(t){t.prototype.toJSON=sa}var nr=class extends bt{createObserver(){return this}bind(e){return this.evaluate(e.source,e.context)}};be(nr);function ar(t,e){return new nr(t,e)}var Vt=class{constructor(e,o){this.sub1=void 0,this.sub2=void 0,this.spillover=void 0,this.subject=e,this.sub1=o}has(e){return this.spillover===void 0?this.sub1===e||this.sub2===e:this.spillover.indexOf(e)!==-1}subscribe(e){let o=this.spillover;if(o===void 0){if(this.has(e))return;if(this.sub1===void 0){this.sub1=e;return}if(this.sub2===void 0){this.sub2=e;return}this.spillover=[this.sub1,this.sub2,e],this.sub1=void 0,this.sub2=void 0}else o.indexOf(e)===-1&&o.push(e)}unsubscribe(e){let o=this.spillover;if(o===void 0)this.sub1===e?this.sub1=void 0:this.sub2===e&&(this.sub2=void 0);else{let r=o.indexOf(e);r!==-1&&o.splice(r,1)}}notify(e){let o=this.spillover,r=this.subject;if(o===void 0){let n=this.sub1,a=this.sub2;n!==void 0&&n.handleChange(r,e),a!==void 0&&a.handleChange(r,e)}else for(let n=0,a=o.length;n<a;++n)o[n].handleChange(r,e)}},Wt=class{constructor(e){this.subscribers={},this.subjectSubscribers=null,this.subject=e}notify(e){var o,r;(o=this.subscribers[e])===null||o===void 0||o.notify(e),(r=this.subjectSubscribers)===null||r===void 0||r.notify(e)}subscribe(e,o){var r,n;let a;o?a=(r=this.subscribers[o])!==null&&r!==void 0?r:this.subscribers[o]=new Vt(this.subject):a=(n=this.subjectSubscribers)!==null&&n!==void 0?n:this.subjectSubscribers=new Vt(this.subject),a.subscribe(e)}unsubscribe(e,o){var r,n;o?(r=this.subscribers[o])===null||r===void 0||r.unsubscribe(e):(n=this.subjectSubscribers)===null||n===void 0||n.unsubscribe(e)}};var Xe=[],dn=[],Ed=globalThis.requestAnimationFrame,pn=!0;function Od(){if(dn.length)throw dn.shift()}function Rd(t){try{t.call()}catch(e){if(pn)dn.push(e),setTimeout(Od,0);else throw Xe.length=0,e}}function un(){let e=0;for(;e<Xe.length;)if(Rd(Xe[e]),e++,e>1024){for(let o=0,r=Xe.length-e;o<r;o++)Xe[o]=Xe[o+e];Xe.length-=e,e=0}Xe.length=0}function ca(t){Xe.push(t),Xe.length<2&&(pn?Ed(un):un())}var O=Object.freeze({enqueue:ca,next:()=>new Promise(ca),process:un,setMode:t=>pn=t});var vt=Object.freeze({unknown:void 0,coupled:1}),k=(()=>{let t=O.enqueue,e=/(:|&&|\|\||if|\?\.)/,o=new WeakMap,r,n=h=>{throw Q.error(q.needsArrayObservation)};function a(h){var u;let v=(u=h.$fastController)!==null&&u!==void 0?u:o.get(h);return v===void 0&&(Array.isArray(h)?v=n(h):o.set(h,v=new Wt(h))),v}let s=rr();class d{constructor(u){this.name=u,this.field=`_${u}`,this.callback=`${u}Changed`}getValue(u){return r!==void 0&&r.watch(u,this.name),u[this.field]}setValue(u,v){let B=this.field,F=u[B];if(F!==v){u[B]=v;let H=u[this.callback];Ge(H)&&H.call(u,F,v),a(u).notify(this.name)}}}class p extends Vt{constructor(u,v,B=!1){super(u,v),this.expression=u,this.isVolatileBinding=B,this.needsRefresh=!0,this.needsQueue=!0,this.isAsync=!0,this.first=this,this.last=null,this.propertySource=void 0,this.propertyName=void 0,this.notifier=void 0,this.next=void 0}setMode(u){this.isAsync=this.needsQueue=u}bind(u){let v=this.observe(u.source,u.context);return!u.isBound&&this.requiresUnbind(u)&&u.onUnbind(this),v}requiresUnbind(u){return u.sourceLifetime!==vt.coupled||this.first!==this.last||this.first.propertySource!==u.source}unbind(u){this.dispose()}observe(u,v){this.needsRefresh&&this.last!==null&&this.dispose();let B=r;r=this.needsRefresh?this:void 0,this.needsRefresh=this.isVolatileBinding;let F;try{F=this.expression(u,v)}finally{r=B}return F}disconnect(){this.dispose()}dispose(){if(this.last!==null){let u=this.first;for(;u!==void 0;)u.notifier.unsubscribe(this,u.propertyName),u=u.next;this.last=null,this.needsRefresh=this.needsQueue=this.isAsync}}watch(u,v){let B=this.last,F=a(u),H=B===null?this.first:{};if(H.propertySource=u,H.propertyName=v,H.notifier=F,F.subscribe(this,v),B!==null){if(!this.needsRefresh){let fe;r=void 0,fe=B.propertySource[B.propertyName],r=this,u===fe&&(this.needsRefresh=!0)}B.next=H}this.last=H}handleChange(){this.needsQueue?(this.needsQueue=!1,t(this)):this.isAsync||this.call()}call(){this.last!==null&&(this.needsQueue=this.isAsync,this.notify(this))}*records(){let u=this.first;for(;u!==void 0;)yield u,u=u.next}}return be(p),Object.freeze({setArrayObserverFactory(h){n=h},getNotifier:a,track(h,u){r&&r.watch(h,u)},trackVolatile(){r&&(r.needsRefresh=!0)},notify(h,u){a(h).notify(u)},defineProperty(h,u){me(u)&&(u=new d(u)),s(h).push(u),Reflect.defineProperty(h,u.name,{enumerable:!0,get(){return u.getValue(this)},set(v){u.setValue(this,v)}})},getAccessors:s,binding(h,u,v=this.isVolatileBinding(h)){return new p(h,u,v)},isVolatileBinding(h){return e.test(h.toString())}})})();function T(t,e){k.defineProperty(t,e)}var da=(()=>{let t=null;return{get(){return t},set(e){t=e}}})(),rt=Object.freeze({default:{index:0,length:0,get event(){return rt.getEvent()},eventDetail(){return this.event.detail},eventTarget(){return this.event.target}},getEvent(){return da.get()},setEvent(t){da.set(t)}});var hn=class extends bt{createObserver(e){return k.binding(this.evaluate,e,this.isVolatile)}};function ir(t,e,o=k.isVolatileBinding(t)){return new hn(t,e,o)}var U=Object.freeze({none:0,attribute:1,booleanAttribute:2,property:3,content:4,tokenList:5,event:6}),ua=t=>t,Md=globalThis.trustedTypes?globalThis.trustedTypes.createPolicy("fast-html",{createHTML:ua}):{createHTML:ua},sr=Object.freeze({createHTML(t){return Md.createHTML(t)},protect(t,e,o,r){return r}}),_d=sr,nt=Object.freeze({get policy(){return sr},setPolicy(t){if(sr!==_d)throw Q.error(q.onlySetDOMPolicyOnce);sr=t},setAttribute(t,e,o){o==null?t.removeAttribute(e):t.setAttribute(e,o)},setBooleanAttribute(t,e,o){o?t.setAttribute(e,""):t.removeAttribute(e)}});var gn="boolean",pa="reflect",jt=Object.freeze({locate:rr()}),ga={toView(t){return t?"":null},fromView(t){return!!t}};function ha(t){if(t==null)return null;let e=t*1;return isNaN(e)?null:e}var ve={toView(t){let e=ha(t);return e&&e.toString()},fromView:ha},No=class t{constructor(e,o,r=o.toLowerCase(),n=pa,a){this.guards=new Set,this.Owner=e,this.name=o,this.attribute=r,this.mode=n,this.converter=a,this.fieldName=`_${o}`,this.callbackName=`${o}Changed`,this.hasCallback=this.callbackName in e.prototype,n===gn&&a===void 0&&(this.converter=ga)}setValue(e,o){let r=e[this.fieldName],n=this.converter;n!==void 0&&(o=n.fromView(o)),r!==o&&(e[this.fieldName]=o,this.tryReflectToAttribute(e),this.hasCallback&&e[this.callbackName](r,o),e.$fastController.notify(this.name))}getValue(e){return k.track(e,this.name),e[this.fieldName]}onAttributeChangedCallback(e,o){this.guards.has(e)||(this.guards.add(e),this.mode===gn?this.setValue(e,o!==null):this.setValue(e,o),this.guards.delete(e))}tryReflectToAttribute(e){let o=this.mode,r=this.guards;r.has(e)||o==="fromView"||O.enqueue(()=>{r.add(e);let n=e[this.fieldName];switch(o){case pa:{let a=this.converter;nt.setAttribute(e,this.attribute,a!==void 0?a.toView(n):n);break}case gn:nt.setBooleanAttribute(e,this.attribute,n);break}r.delete(e)})}static collect(e,...o){let r=[];o.push(jt.locate(e));for(let n=0,a=o.length;n<a;++n){let s=o[n];if(s!==void 0)for(let d=0,p=s.length;d<p;++d){let h=s[d];me(h)?r.push(new t(e,h)):r.push(new t(e,h.property,h.attribute,h.mode,h.converter))}}return r}};function l(t,e){let o;function r(n,a){arguments.length>1&&(o.property=a),jt.locate(n.constructor).push(o)}if(arguments.length>1){o={},r(t,e);return}return o=t===void 0?{}:t,r}var fn;function fa(t){return t.reduce((e,o)=>(o instanceof ke?e.push(...fa(o.styles)):e.push(o),e),[])}var ke=class t{get strategy(){return this._strategy===null&&(fn||t.setDefaultStrategy(t.supportsAdoptedStyleSheets?Vd():jd()),this.withStrategy(fn)),this._strategy}constructor(e){this.styles=e,this.targets=new WeakSet,this._strategy=null}addStylesTo(e){this.strategy.addStylesTo(e),this.targets.add(e)}removeStylesFrom(e){this.strategy.removeStylesFrom(e),this.targets.delete(e)}isAttachedTo(e){return this.targets.has(e)}withStrategy(e){return this._strategy=new e(fa(this.styles)),this}static setDefaultStrategy(e){fn=e}static normalize(e){return e===void 0?void 0:Array.isArray(e)?new t(e):e instanceof t?e:new t([e])}};ke.supportsAdoptedStyleSheets=Array.isArray(document.adoptedStyleSheets)&&"replace"in CSSStyleSheet.prototype;function Vd(){let t=new Map;return class{constructor(o){this.sheets=o.map(r=>{if(r instanceof CSSStyleSheet)return r;let n=t.get(r);return n===void 0&&(n=new CSSStyleSheet,n.replaceSync(r),t.set(r,n)),n})}addStylesTo(o){let r=o;r.adoptedStyleSheets=[...r.adoptedStyleSheets,...this.sheets]}removeStylesFrom(o){let r=o;r.adoptedStyleSheets=r.adoptedStyleSheets.filter(n=>this.sheets.indexOf(n)===-1)}}}var Wd=0;function jd(){return class{constructor(e){this.styles=e,this.styleClass=`fast-${++Wd}`}addStylesTo(e){let o=e===document?document.body:e;for(let r=0;r<this.styles.length;r++){let n=document.createElement("style");n.innerHTML=this.styles[r],n.className=this.styleClass,o.append(n)}}removeStylesFrom(e){let o=e===document?document.body:e,r=o.querySelectorAll(`.${this.styleClass}`);for(let n=0,a=r.length;n<a;++n)o.removeChild(r[n])}}}var mn={define(t,e,o,r,n){r.define(t,e,o)}};var Gd={},ma=new WeakMap,bn=_t(),lr=new WeakMap,ba=new WeakMap,Gt=Object.freeze({register(t){if(!bn.register(t))return!1;let e=ka(t.registry);return Object.prototype.hasOwnProperty.call(e,t.name)||k.defineProperty(e,t.name),e[t.name]=t.type,!0},getByType:bn.getByType,getForInstance:bn.getForInstance,whenRegistered:Xd});function ka(t=customElements){if(t===customElements)return Gd;let e=ma.get(t);return e||(e={},ma.set(t,e)),e}function va(t){return t===void 0?void 0:Gt.getByType(t)}function Xd(t,e=customElements){let o=ka(e);Object.prototype.hasOwnProperty.call(o,t)||k.defineProperty(o,t);let r=va(o[t]);return r!==void 0?Promise.resolve(r):new Promise(n=>{let a=k.getNotifier(o),s={handleChange:()=>{let d=va(o[t]);d!==void 0&&(a.unsubscribe(s,t),n(d))}};a.subscribe(s,t)})}function ya(t,e){if(ba.has(e))return!1;let o=lr.get(t),r=o===void 0;o===void 0&&(o=new Set,lr.set(t,o));let n=new WeakRef(e);return o.add(n),ba.set(e,n),r}function xa(t,e){let o=lr.get(t);if(o!==void 0){lr.delete(t);for(let r of o){let n=r.deref();n!==void 0&&e(n)}}}var Sa={mode:"open"},Ba={},Na=new Set,dr=new WeakMap,cr=new WeakMap,Pt=new WeakMap,$a=new WeakMap,qd=new WeakMap;function Ud(t){return Ge(t)}function Kd(t){return typeof t?.then=="function"}function Pa(t,e){if(cr.delete(t),t.template===void 0&&e!==void 0&&(t.template=e),t.template!==void 0)return Pt.delete(t),dr.delete(t),t.template}function vn(t,e=t.registry,o){if(!o?.length)return;let r=t,n=$a.get(r);if(!n?.has(e)){n===void 0&&(n=new WeakSet,$a.set(r,n)),n.add(e);for(let a of o)a(t)}}function ur(t){var e;return(e=qd.get(t))!==null&&e!==void 0?e:null}function kn(t){if(t.template!==void 0)return Pt.delete(t),t.template;let e=cr.get(t);if(e)return e;let o=dr.get(t);if(!o)return;Pt.delete(t);let r;try{r=o(t)}catch(n){throw Pt.set(t,n),n}if(Kd(r)){let n=Promise.resolve(r).then(a=>Pa(t,a)).catch(a=>{throw cr.delete(t),Pt.set(t,a),a});return cr.set(t,n),n}return Pa(t,r)}function Yd(t,e){let o=t;if(e===void 0){Pt.delete(o);return}Pt.set(o,e)}var Ee=class t{get isDefined(){return this.platformDefined}constructor(e,o=e.definition){var r;this.platformDefined=!1,me(o)&&(o={name:o}),this.type=e,this.name=o.name,this.registry=(r=o.registry)!==null&&r!==void 0?r:customElements,Ud(o.template)?dr.set(this,o.template):this.template=o.template;let n=e.prototype,a=No.collect(e,o.attributes),s=new Array(a.length),d={},p={};for(let h=0,u=a.length;h<u;++h){let v=a[h];s[h]=v.attribute,d[v.name]=v,p[v.attribute]=v,k.defineProperty(n,v)}Reflect.defineProperty(e,"observedAttributes",{value:s,enumerable:!0}),this.attributes=a,this.propertyLookup=d,this.attributeLookup=p,this.shadowOptions=o.shadowOptions===void 0?Sa:o.shadowOptions===null?void 0:Object.assign(Object.assign({},Sa),o.shadowOptions),this.elementOptions=o.elementOptions===void 0?Ba:Object.assign(Object.assign({},Ba),o.elementOptions),this.styles=ke.normalize(o.styles),this.schema=o.schema,Gt.register(this)}define(e=this.registry,o){let r=this.type;if(!e.get(this.name)){if(vn(this,e,o),this.template===void 0&&dr.has(this))return Promise.resolve().then(()=>kn(this)).then(a=>{if(a!==void 0&&!e.get(this.name)){let s=this.platformDefined;this.platformDefined=!0,mn.define(this.name,r,this.elementOptions,e,s)}}).catch(a=>{Yd(this,a),k.notify(this,"template")}),this;let n=this.platformDefined;this.platformDefined=!0,mn.define(this.name,r,this.elementOptions,e,n)}return this}static compose(e,o){let r=Na.has(e)||Gt.getByType(e)?new t(class extends e{},o):new t(e,o);return Promise.resolve(r)}static registerBaseType(e){Na.add(e)}};Ee.getByType=Gt.getByType;Ee.getForInstance=Gt.getForInstance;k.defineProperty(Ee.prototype,"template");var Jd={bubbles:!0,composed:!0,cancelable:!0},yn="isConnected",Ta=new WeakMap,xn=Symbol("fast-late-attribute-observer");function pr(t){var e,o;return(o=(e=t.shadowRoot)!==null&&e!==void 0?e:Ta.get(t))!==null&&o!==void 0?o:null}var wa,ye;(function(t){t[t.connecting=0]="connecting",t[t.connected=1]="connected",t[t.disconnecting=2]="disconnecting",t[t.disconnected=3]="disconnected"})(ye||(ye={}));var kt=class t{get isConnected(){return k.track(this,yn),this.stage===ye.connected}get context(){var e,o;return(o=(e=this.view)===null||e===void 0?void 0:e.context)!==null&&o!==void 0?o:rt.default}get isBound(){var e,o;return(o=(e=this.view)===null||e===void 0?void 0:e.isBound)!==null&&o!==void 0?o:!1}get sourceLifetime(){var e;return(e=this.view)===null||e===void 0?void 0:e.sourceLifetime}get template(){var e;if(this._template===null){let o=this.definition;this.source.resolveTemplate?this._template=this.source.resolveTemplate():o.template&&(this._template=(e=o.template)!==null&&e!==void 0?e:null)}return this._template}set template(e){this._template!==e&&(this._template=e,this.needsInitialization||this.renderTemplate(e))}get shadowOptions(){return this._shadowRootOptions}set shadowOptions(e){if(this._shadowRootOptions===void 0&&e!==void 0){this._shadowRootOptions=e;let o=this.source.shadowRoot;o?this.hasExistingShadowRoot=!0:(o=this.source.attachShadow(e),e.mode==="closed"&&Ta.set(this.source,o))}}get mainStyles(){var e;if(this._mainStyles===null){let o=this.definition;this.source.resolveStyles?this._mainStyles=this.source.resolveStyles():o.styles&&(this._mainStyles=(e=o.styles)!==null&&e!==void 0?e:null)}return this._mainStyles}set mainStyles(e){this._mainStyles!==e&&(this._mainStyles!==null&&this.removeStyles(this._mainStyles),this._mainStyles=e,this.needsInitialization||this.addStyles(e))}constructor(e,o){this.boundObservables=null,this.needsInitialization=!0,this.hasExistingShadowRoot=!1,this.isPrerendered=new Promise(a=>{this._resolvePrerendered=a}),this.isHydrated=new Promise(a=>{this._resolveHydrated=a}),this._template=null,this.stage=ye.disconnected,this.guardBehaviorConnection=!1,this.behaviors=null,this.behaviorsConnected=!1,this._mainStyles=null,this.$fastController=this,this.view=null,this._notifier=new Wt(e),this.source=e,this.definition=o,this.shadowOptions=o.shadowOptions;let r=Reflect.getPrototypeOf(e),n=r===null?[]:k.getAccessors(r);if(n.length>0){let a=this.boundObservables=Object.create(null);for(let s=0,d=n.length;s<d;++s){let p=n[s].name,h=e[p];h!==void 0&&(delete e[p],a[p]=h)}Object.keys(a).length===0&&(this.boundObservables=null)}this.captureBoundObservables()}get subject(){return this._notifier.subject}notify(e){this._notifier.notify(e)}subscribe(e,o){this._notifier.subscribe(e,o)}unsubscribe(e,o){this._notifier.unsubscribe(e,o)}onUnbind(e){var o;(o=this.view)===null||o===void 0||o.onUnbind(e)}addBehavior(e){var o,r;let n=(o=this.behaviors)!==null&&o!==void 0?o:this.behaviors=new Map,a=(r=n.get(e))!==null&&r!==void 0?r:0;a===0?(n.set(e,1),e.addedCallback&&e.addedCallback(this),e.connectedCallback&&!this.guardBehaviorConnection&&(this.stage===ye.connected||this.stage===ye.connecting)&&e.connectedCallback(this)):n.set(e,a+1)}removeBehavior(e,o=!1){let r=this.behaviors;if(r===null)return;let n=r.get(e);n!==void 0&&(n===1||o?(r.delete(e),e.disconnectedCallback&&this.stage!==ye.disconnected&&e.disconnectedCallback(this),e.removedCallback&&e.removedCallback(this)):r.set(e,n-1))}addStyles(e){var o;if(!e)return;let r=this.source;e instanceof HTMLElement?((o=pr(r))!==null&&o!==void 0?o:this.source).append(e):e.isAttachedTo(r)||e.addStylesTo(r)}removeStyles(e){var o;if(!e)return;let r=this.source;e instanceof HTMLElement?((o=pr(r))!==null&&o!==void 0?o:r).removeChild(e):e.isAttachedTo(r)&&e.removeStylesFrom(r)}connect(){this.stage===ye.disconnected&&(this.stage=ye.connecting,this.captureBoundObservables(),this.syncLateAttributes(),this.observeLateAttributes(),this.bindObservables(),this.connectBehaviors(),this.needsInitialization?(this.renderTemplate(this.template),this.addStyles(this.mainStyles),this.needsInitialization=!1):this.view!==null&&this.view.bind(this.source),this.stage=ye.connected,k.notify(this,yn))}bindObservables(){if(this.boundObservables!==null){let e=this.source,o=this.boundObservables,r=Object.keys(o);for(let n=0,a=r.length;n<a;++n){let s=r[n];e[s]=o[s]}this.boundObservables=null}}captureBoundObservables(){let e=this.source,o=Object.getOwnPropertyNames(e),r=a=>{let s=Reflect.getPrototypeOf(e);for(;s!==null;){let d=Reflect.getOwnPropertyDescriptor(s,a);if(d?.get||d?.set)return!0;s=Reflect.getPrototypeOf(s)}return!1},n=this.boundObservables;for(let a=0,s=o.length;a<s;++a){let p=o[a];if(!r(p))continue;let h=e[p];if(h===void 0){delete e[p];continue}delete e[p],(n??(n=this.boundObservables=Object.create(null)))[p]=h}}syncLateAttributes(){let e=ur(this.definition);if(e!==null)for(let o of Object.keys(e))this.source.hasAttribute(o)&&this.onAttributeChangedCallback(o,null,this.source.getAttribute(o))}observeLateAttributes(){let e=ur(this.definition);if(e===null)return;let o=this.source;o[xn]===void 0&&(o[xn]=new MutationObserver(r=>{let n=o.$fastController,a=ur(n.definition);if(a!==null)for(let s=0,d=r.length;s<d;++s){let p=r[s].attributeName;p===null||a[p]===void 0||n.onAttributeChangedCallback(p,null,o.getAttribute(p))}}),o[xn].observe(o,{attributes:!0,attributeFilter:Object.keys(e)}))}connectBehaviors(){if(this.behaviorsConnected===!1){let e=this.behaviors;if(e!==null){this.guardBehaviorConnection=!0;for(let o of e.keys())o.connectedCallback&&o.connectedCallback(this);this.guardBehaviorConnection=!1}this.behaviorsConnected=!0}}disconnectBehaviors(){if(this.behaviorsConnected===!0){let e=this.behaviors;if(e!==null)for(let o of e.keys())o.disconnectedCallback&&o.disconnectedCallback(this);this.behaviorsConnected=!1}}disconnect(){this.stage===ye.connected&&(this.stage=ye.disconnecting,k.notify(this,yn),this.view!==null&&this.view.unbind(),this.disconnectBehaviors(),this.stage=ye.disconnected)}onAttributeChangedCallback(e,o,r){let n=this.definition.attributeLookup[e];n!==void 0&&n.onAttributeChangedCallback(this.source,r)}emit(e,o,r){return this.stage===ye.connected?this.source.dispatchEvent(new CustomEvent(e,Object.assign(Object.assign({detail:o},Jd),r))):!1}renderTemplate(e){var o;let r=this.source,n=(o=pr(r))!==null&&o!==void 0?o:r;if(this.view!==null)this.view.dispose(),this.view=null;else if((!this.needsInitialization||this.hasExistingShadowRoot)&&(!this.hasExistingShadowRoot||!this.needsInitialization))for(let a=n.firstChild;a!==null;a=n.firstChild)n.removeChild(a);if(e){let a=this.hasExistingShadowRoot&&this.needsInitialization,s=!1;a&&t.hydrationHook&&(s=t.hydrationHook(this,e,r,n)),s||this.renderClientSide(e,r,n),this._resolvePrerendered(a),this._resolveHydrated(s)}else this.needsInitialization&&(this._resolvePrerendered(!1),this._resolveHydrated(!1))}renderClientSide(e,o,r){if(this.hasExistingShadowRoot){for(let n=r.firstChild;n!==null;n=r.firstChild)r.removeChild(n);this.hasExistingShadowRoot=!1}this.view=e.render(o,r,o),this.view.sourceLifetime=vt.coupled}static forCustomElement(e,o=!1){let r=e.$fastController;if(r!==void 0&&!o)return r;let n=Ee.getForInstance(e);if(n===void 0)throw Q.error(q.missingElementDefinition);if(n.template===void 0&&ya(n,e)){let a=k.getNotifier(n),s={handleChange:()=>{a.unsubscribe(s,"template"),xa(n,d=>{let p=d;t.forCustomElement(p,!0),p.isConnected&&p.$fastController.connect()})}};a.subscribe(s,"template")}return e.$fastController=new wa(e,n)}static setStrategy(e){wa=e}static installHydrationHook(e){t.hydrationHook=e}};kt.hydrationHook=null;be(kt);kt.setStrategy(kt);function hr(t){var e;return"adoptedStyleSheets"in t?t:(e=pr(t))!==null&&e!==void 0?e:t.getRootNode()}var gr=class t{constructor(e){let o=t.styleSheetCache;this.sheets=e.map(r=>{if(r instanceof CSSStyleSheet)return r;let n=o.get(r);return n===void 0&&(n=new CSSStyleSheet,n.replaceSync(r),o.set(r,n)),n})}addStylesTo(e){Aa(hr(e),this.sheets)}removeStylesFrom(e){Ca(hr(e),this.sheets)}};gr.styleSheetCache=new Map;var Qd=0,Zd=()=>`fast-${++Qd}`;function Fa(t){return t===document?document.body:t}var Sn=class{constructor(e){this.styles=e,this.styleClass=Zd()}addStylesTo(e){e=Fa(hr(e));let o=this.styles,r=this.styleClass;for(let n=0;n<o.length;n++){let a=document.createElement("style");a.innerHTML=o[n],a.className=r,e.append(a)}}removeStylesFrom(e){e=Fa(hr(e));let o=e.querySelectorAll(`.${this.styleClass}`);for(let r=0,n=o.length;r<n;++r)e.removeChild(o[r])}},Aa=(t,e)=>{t.adoptedStyleSheets=[...t.adoptedStyleSheets,...e]},Ca=(t,e)=>{t.adoptedStyleSheets=t.adoptedStyleSheets.filter(o=>e.indexOf(o)===-1)};if(ke.supportsAdoptedStyleSheets){try{document.adoptedStyleSheets.push(),document.adoptedStyleSheets.splice(),Aa=(t,e)=>{t.adoptedStyleSheets.push(...e)},Ca=(t,e)=>{for(let o of e){let r=t.adoptedStyleSheets.indexOf(o);r!==-1&&t.adoptedStyleSheets.splice(r,1)}}}catch{}ke.setDefaultStrategy(gr)}else ke.setDefaultStrategy(Sn);function Ia(t){let e=class extends t{constructor(){super(),kt.forCustomElement(this)}$emit(o,r,n){return this.$fastController.emit(o,r,n)}connectedCallback(){this.$fastController.connect()}disconnectedCallback(){this.$fastController.disconnect()}attributeChangedCallback(o,r,n){this.$fastController.onAttributeChangedCallback(o,r,n)}};return Ee.registerBaseType(e),e}function eu(t){return typeof t?.then=="function"}function tu(t,e,o){return Array.isArray(e)&&(o=e,e=void 0),(Ge(t)?Ee.compose(t,e):Ee.compose(this,t)).then(n=>{vn(n,n.registry,o);let a=kn(n);return eu(a)?a.then(()=>n.define().type):n.define().type})}function ou(t){return Ia(t)}var y=Object.assign(Ia(HTMLElement),{from:ou,define:tu});function wt(t,e,o){return Object.assign({},o,{get(){return k.trackVolatile(),o.get.apply(this)}})}var Bn=_t(),Nn=Object.freeze({getForInstance:Bn.getForInstance,getByType:Bn.getByType,define(t){return Bn.register({type:t}),t}});function Ha(t,e){let o=[],r="";for(let n=0,a=t.length-1;n<a;++n){r+=t[n];let s=e[n];Nn.getForInstance(s)!==void 0&&(s=s.createCSS()),s instanceof ke||s instanceof CSSStyleSheet?(r.trim()!==""&&(o.push(r),r=""),o.push(s)):r+=s}return r+=t[t.length-1],r.trim()!==""&&o.push(r),o}var f=((t,...e)=>new ke(Ha(t,e))),fr=class{constructor(e){this.value=e.length===0?"":e.length===1?e[0]:new ke(e)}createCSS(){return this.value}};Nn.define(fr);f.partial=(t,...e)=>new fr(Ha(t,e));var $n=`fast-${Math.random().toString(36).substring(2,8)}`,mr=`${$n}{`,$o=`}${$n}`,ru=$o.length,nu=0,Po=()=>`${$n}-${++nu}`,br=Object.freeze({interpolation:t=>`${mr}${t}${$o}`,attribute:t=>`${Po()}="${mr}${t}${$o}"`,comment:t=>`<!--${mr}${t}${$o}-->`}),wo=Object.freeze({parse(t,e){let o=t.split(mr);if(o.length===1)return null;let r=[];for(let n=0,a=o.length;n<a;++n){let s=o[n],d=s.indexOf($o),p;if(d===-1)p=s;else{let h=s.substring(0,d);r.push(e[h]),p=s.substring(d+ru)}p!==""&&r.push(p)}return r}});var Pn=_t(),ce=Object.freeze({getForInstance:Pn.getForInstance,getByType:Pn.getByType,define(t,e){return e=e||{},e.type=t,Pn.register(e),t},assignAspect(t,e){if(!e){t.aspectType=U.content;return}switch(t.sourceAspect=e,e[0]){case":":t.targetAspect=e.substring(1),t.aspectType=t.targetAspect==="classList"?U.tokenList:U.property;break;case"?":t.targetAspect=e.substring(1),t.aspectType=U.booleanAttribute;break;case"@":t.targetAspect=e.substring(1),t.aspectType=U.event;break;default:t.targetAspect=e,t.aspectType=U.attribute;break}}});var Ft=class{constructor(e){this.options=e}createHTML(e){return br.attribute(e(this))}createBehavior(){return this}};be(Ft);var au=t=>t.nodeType===1,wn=t=>t?e=>e.nodeType===1&&e.matches(t):au,Fo=class extends Ft{get id(){return this._id}set id(e){this._id=e,this._controllerProperty=`${e}-c`}bind(e){let o=e.targets[this.targetNodeId];o[this._controllerProperty]=e,this.updateTarget(e.source,this.computeNodes(o)),this.observe(o),e.onUnbind(this)}unbind(e){let o=e.targets[this.targetNodeId];this.updateTarget(e.source,la),this.disconnect(o),o[this._controllerProperty]=null}getSource(e){return e[this._controllerProperty].source}updateTarget(e,o){e[this.options.property]=o}computeNodes(e){let o=this.getNodes(e);return"filter"in this.options&&(o=o.filter(this.options.filter)),o}};var iu=Symbol(),La=new WeakMap;function su(t){let e=Object.freeze(Object.assign(Object.assign({},t),{[iu]:!0}));return La.set(e,e),e}function za(t){return t.nodeType===Node.COMMENT_NODE}function Da(t){let e=t.getAttribute("data-fe");if(e===null)return null;let o=e.trim();if(!/^\d+$/.test(o))throw Q.error(q.invalidHydrationAttributeMarker,{value:e});let r=parseInt(o,10);if(r<1)throw Q.error(q.invalidHydrationAttributeMarker,{value:e});return r}var Xt=su({attributeMarkerName:"data-fe",contentBindingStartMarker(){return"fe:b"},contentBindingEndMarker(){return"fe:/b"},repeatStartMarker(){return"fe:r"},repeatEndMarker(){return"fe:/r"},elementBoundaryStartMarker(){return"fe:e"},elementBoundaryEndMarker(){return"fe:/e"},parseAttributeBindingCount:Da,resolveAttributeBindings(t,e,o){let r=Da(t);return r!==null?{factoryIndices:Array.from({length:r},(a,s)=>e+s),nextFactoryPointer:e+r,cleanup:()=>t.removeAttribute("data-fe")}:null},resolveContentBinding(t,e,o){return t==="fe:b"?{factoryIndices:[e],nextFactoryPointer:e+1}:null},isContentBindingStartMarker(t){return t==="fe:b"},isContentBindingEndMarker(t){return t==="fe:/b"},isRepeatViewStartMarker(t){return t==="fe:r"},isRepeatViewEndMarker(t){return t==="fe:/r"},isElementBoundaryStartMarker(t){return za(t)&&t.data==="fe:e"},isElementBoundaryEndMarker(t){return za(t)&&t.data==="fe:/e"}}),lu=La.get(Xt);function Ea(){return lu}var To=Symbol.for("fe-hydration");function vr(t){return t[To]===To}var Oa="unknown",Ra="content following `<!--fe:b-->` content binding marker",Ma="matching `<!--fe:/b-->` content binding close marker",_a="matching `<!--fe:/e-->` element boundary close marker";function Va(t,e){let o=e?`: ${e}`:"";return`Hydration mismatch in <${t}>${o}. Install hydrationDebugger() from "@microsoft/fast-element/hydration.js" and pass it as enableHydration({ debugger: hydrationDebugger() }) for an "Expected / Received" report including the SSR HTML snippet.`}function Fn(t){return`no more attribute bindings (template defines ${t})`}function Wa(t){return`no more content bindings (template defines ${t})`}function ja(t,e){let o=(t??Oa).toLowerCase();return Va(o,e)}var cu={formatBindingMismatch(t,e,o,r){return{message:ja(r,void 0)}},formatStructuralError(t,e,o){return{message:ja(e,o)}}},du=cu;function kr(){return du}function yr(t){var e;return t?(e=t.getRootNode().host)===null||e===void 0?void 0:e.nodeName:void 0}var Tn,An,Hh=(Tn=Object.getOwnPropertyDescriptor(Object.getPrototypeOf(customElements),"define"))===null||Tn===void 0?void 0:Tn.value,zh=(An=Object.getOwnPropertyDescriptor(Array.prototype,Symbol.iterator))===null||An===void 0?void 0:An.value,uu=new WeakMap;function Ga(t){return uu.get(Object.getPrototypeOf(t))===!0}function xr(t){return t.nodeType===8}function Cn(t){return t.nodeType===3}function qa(t){return xr(t)||Cn(t)?t.data.length:t.childNodes.length}var Ua=133;function In(t,e){let o=document.createRange();return o.setStart(t,0),o.setEnd(e,qa(e)),o}function Xa(t,e,o=t){let r=In(t,e),n=r.startContainer===t?document.createTreeWalker(r.commonAncestorContainer,Ua):null,a=e.contains(o),s=!1;n!==null&&(n.currentNode=o);function d(p){if(n===null||!s&&p&&n.currentNode.contains(e))return null;let h=n.currentNode,u=p?n.nextSibling():n.nextNode();return s||!a||e.contains(u)?(u===e&&(a=!0),u):(n.currentNode=h,null)}return[d,()=>{if(n!==null&&!s&&(r.startContainer!==t||r.endContainer!==e||r.endOffset!==qa(e)||n.currentNode!==t&&n.currentNode!==e&&!r.intersectsNode(n.currentNode))){let p=n.currentNode;n=document.createTreeWalker(n.root,n.whatToShow,{acceptNode:h=>r.comparePoint(h,0)===0?NodeFilter.FILTER_ACCEPT:NodeFilter.FILTER_REJECT}),n.currentNode=p,s=!0}}]}function Ka(t,e){let o=t.parentNode;if(o===null||o!==e.parentNode||t!==e&&!(t.compareDocumentPosition(e)&Node.DOCUMENT_POSITION_FOLLOWING)){let[s,d]=Xa(t,e);return[s,d,()=>{}]}let r=t,n=null;function a(s=!1){if(n!==null)return n[0](s);if(s&&r===e)return null;let d=r;do if(s){if(d===e)return null;d=d.nextSibling}else{let p=d;for(d=d.firstChild;d===null;){if(p===e)return null;if(d=p.nextSibling,p=p.parentNode,p===o||p===null)break}}while(d!==null&&!(Ua&1<<d.nodeType-1));return d!==null&&(r=d),d}return[a,()=>n?.[1](),()=>{n??(n=Xa(t,e,r))}]}var Ao=class extends Error{constructor(e,o,r,n,a){super(e),this.factories=o,this.node=r,this.expected=n,this.received=a}},Ya=Element.prototype.removeAttribute,Ja=Element.prototype.getAttribute,pu=Element.prototype.hasAttribute;function Za(t,e,o){let r=t;for(;r!==null;){let n=Object.getOwnPropertyDescriptor(r,e);if(n!==void 0)return n.value===o;r=Object.getPrototypeOf(r)}return!1}function Qa(t,e){return e?pu.call(t,"is")||!Za(t,"removeAttribute",Ya):t.localName.includes("-")||t.hasAttribute("is")||"attributeChangedCallback"in t||t.removeAttribute!==Ya}function qt(t,e,o){let r=kr().formatStructuralError(t,yr(t),o);throw new Ao(r.message,e,t,r.expected,r.received)}function ei(t,e,o){var r;let[n,a,s]=Ka(t,e),d={},p={},h=Ea(),u=h===Xt;h!==Xt&&s();let v=fu(o),B=v,F=t;for(;F!==null;){switch(F.nodeType){case Node.ELEMENT_NODE:{let H=F,fe=u&&H.localName.includes("-"),or=fe&&Ga(H);if((fe?!Za(H,"getAttribute",Ja):H.getAttribute!==Ja)&&s(),u){let So=Xt.parseAttributeBindingCount(H);if(So!==null){So>4294967295&&Xt.resolveAttributeBindings(H,B,v);for(let Bo=0;Bo<So;Bo++){let ia=o[B++];ia||qt(F,o,Fn(o.length)),Ut(ia,F,d)}Qa(H,or)&&s(),H.removeAttribute("data-fe"),a()}break}let ot=h.resolveAttributeBindings(H,B,v);if(ot!==null){B=ot.nextFactoryPointer;for(let So of ot.factoryIndices){let Bo=o[So];Bo||qt(F,o,Fn(o.length)),Ut(Bo,F,d)}Qa(H,!1)&&s(),(r=ot.cleanup)===null||r===void 0||r.call(ot),a()}break}case 8:{let H=F.data;if(u?H==="fe:e":h.isElementBoundaryStartMarker(F))F.data="",gu(n,o,F,h,u);else{let fe;if(u){if(H!=="fe:b")break;fe=B++}else{let ot=h.resolveContentBinding(H,B,v);if(ot===null)break;B=ot.nextFactoryPointer,fe=ot.factoryIndices[0]}let or=o[fe];or||qt(F,o,Wa(o.length)),hu(F,n,or,o,d,p,h,u)}break}}F=n()}return{targets:d,boundaries:p}}function hu(t,e,o,r,n,a,s,d){let p=e(!0);t.data="",p===null&&qt(t,r,Ra);let h=p,u=0;for(;p!==null;){if(xr(p)){if(d?p.data==="fe:b":s.isContentBindingStartMarker(p.data))u++;else if(d?p.data==="fe:/b":s.isContentBindingEndMarker(p.data)){if(u===0)break;u--}}p=e(!0)}p===null&&qt(t,r,Ma),p.data="",Cn(h)&&h.nextSibling===p?Ut(o,h,n):(p!==h&&p.previousSibling!==null&&(a[o.targetNodeId]={first:h,last:p.previousSibling}),Ut(o,p.parentNode.insertBefore(document.createTextNode(""),p),n))}function gu(t,e,o,r,n){let a=0,s=t(!0);for(;s!==null;){if(xr(s)){if(n?s.data==="fe:e":r.isElementBoundaryStartMarker(s))s.data="",a++;else if((n?s.data==="fe:/e":r.isElementBoundaryEndMarker(s))&&(s.data="",a--===0))return}s=t(!0)}qt(o,e,_a)}function fu(t){let e=0;for(;e<t.length&&t[e].targetNodeId==="h";)e++;return e}function Ut(t,e,o){let r=t.targetNodeId;if(r===void 0)throw new Error("Factory could not be target to the node");o[r]=e}function Kt(t,e){let o=t.parentNode,r=t,n;for(;r!==e;){if(n=r.nextSibling,!n)throw new Error(`Unmatched first/last child inside "${e.getRootNode().host.nodeName}".`);o.removeChild(r),r=n}o.removeChild(e)}var Co=class{constructor(){this.index=0,this.length=0}get event(){return rt.getEvent()}get isEven(){return this.index%2===0}get isOdd(){return this.index%2!==0}get isFirst(){return this.index===0}get isInMiddle(){return!this.isFirst&&!this.isLast}get isLast(){return this.index===this.length-1}eventDetail(){return this.event.detail}eventTarget(){return this.event.target}},Tt=class extends Co{constructor(e,o,r){super(),this.fragment=e,this.factories=o,this.targets=r,this.behaviors=null,this.unbindables=[],this.source=null,this.isBound=!1,this.sourceLifetime=vt.unknown,this._skipAttrUpdates=!1,this.isPrerendered=Promise.resolve(!1),this.isHydrated=Promise.resolve(!1),this.context=this,this.firstChild=e.firstChild,this.lastChild=e.lastChild}appendTo(e){e.appendChild(this.fragment)}insertBefore(e){if(this.fragment.hasChildNodes())e.parentNode.insertBefore(this.fragment,e);else{let o=this.lastChild;if(e.previousSibling===o)return;let r=e.parentNode,n=this.firstChild,a;for(;n!==o;)a=n.nextSibling,r.insertBefore(n,e),n=a;r.insertBefore(o,e)}}remove(){let e=this.fragment,o=this.lastChild,r=this.firstChild,n;for(;r!==o;)n=r.nextSibling,e.appendChild(r),r=n;e.appendChild(o)}dispose(){Kt(this.firstChild,this.lastChild),this.unbind()}onUnbind(e){this.unbindables.push(e)}bind(e,o=this){if(this.source===e&&this.context===o)return;let r=this.behaviors;if(r===null){this.source=e,this.context=o,this.behaviors=r=new Array(this.factories.length);let n=this.factories;for(let a=0,s=n.length;a<s;++a){let d=n[a].createBehavior();d.bind(this),r[a]=d}}else{this.source!==null&&this.evaluateUnbindables(),this.isBound=!1,this.source=e,this.context=o;for(let n=0,a=r.length;n<a;++n)r[n].bind(this)}this.isBound=!0}unbind(){!this.isBound||this.source===null||(this.evaluateUnbindables(),this.source=null,this.context=this,this.isBound=!1)}evaluateUnbindables(){let e=this.unbindables;for(let o=0,r=e.length;o<r;++o)e[o].unbind(this);e.length=0}static disposeContiguousBatch(e){if(e.length!==0){Kt(e[0].firstChild,e[e.length-1].lastChild);for(let o=0,r=e.length;o<r;++o)e[o].unbind()}}};be(Tt);k.defineProperty(Tt.prototype,"index");k.defineProperty(Tt.prototype,"length");var ti,At={unhydrated:"unhydrated",hydrating:"hydrating",hydrated:"hydrated"},Hn=class extends Error{constructor(e,o,r,n,a,s){super(e),this.factory=o,this.fragment=r,this.templateString=n,this.expected=a,this.received=s}},zn=class extends Co{get hydrationStage(){return this._hydrationStage}get targets(){return this._targets}get bindingViewBoundaries(){return this._bindingViewBoundaries}constructor(e,o,r,n){super(),this.firstChild=e,this.lastChild=o,this.sourceTemplate=r,this.hostBindingTarget=n,this[ti]=To,this.context=this,this.source=null,this.isBound=!1,this.sourceLifetime=vt.unknown,this.unbindables=[],this.fragment=null,this.behaviors=null,this._hydrationStage=At.unhydrated,this._bindingViewBoundaries={},this._targets={},this.factories=r.compile().factories}insertBefore(e){if(this.fragment!==null)if(this.fragment.hasChildNodes())e.parentNode.insertBefore(this.fragment,e);else{let o=this.lastChild;if(e.previousSibling===o)return;let r=e.parentNode,n=this.firstChild,a;for(;n!==o;)a=n.nextSibling,r.insertBefore(n,e),n=a;r.insertBefore(o,e)}}appendTo(e){this.fragment!==null&&e.appendChild(this.fragment)}remove(){let e=this.fragment||(this.fragment=document.createDocumentFragment()),o=this.lastChild,r=this.firstChild,n;for(;r!==o;){if(n=r.nextSibling,!n)throw new Error(`Unmatched first/last child inside "${o.getRootNode().host.nodeName}".`);e.appendChild(r),r=n}e.appendChild(o)}bind(e,o=this){if(this.source===e&&this.context===o)return;this.hydrationStage!==At.hydrated&&(this._hydrationStage=At.hydrating);let r=this.behaviors;if(r===null){this.source=e,this.context=o;try{let{targets:a,boundaries:s}=ei(this.firstChild,this.lastChild,this.factories);this._targets=a,this._bindingViewBoundaries=s}catch(a){if(a instanceof Ao){let s=this.sourceTemplate.html;typeof s!="string"&&(s=s.innerHTML),a.templateString=s}throw a}this.behaviors=r=new Array(this.factories.length);let n=this.factories;for(let a=0,s=n.length;a<s;++a){let d=n[a];if(d.targetNodeId==="h"&&this.hostBindingTarget&&Ut(d,this.hostBindingTarget,this._targets),d.targetNodeId in this.targets){let p=d.createBehavior();p.bind(this),r[a]=p}else{let p=this.sourceTemplate.html;typeof p!="string"&&(p=p.innerHTML);let h=In(this.firstChild,this.lastChild).cloneContents(),u=kr().formatBindingMismatch(d,this.firstChild,this.lastChild,yr(this.firstChild));throw new Hn(u.message,d,h,p,u.expected,u.received)}}}else{this.source!==null&&this.evaluateUnbindables(),this.isBound=!1,this.source=e,this.context=o;for(let n=0,a=r.length;n<a;++n)r[n].bind(this)}this.isBound=!0,this._hydrationStage=At.hydrated}unbind(){!this.isBound||this.source===null||(this.evaluateUnbindables(),this.source=null,this.context=this,this.isBound=!1)}dispose(){Kt(this.firstChild,this.lastChild),this.unbind()}onUnbind(e){this.unbindables.push(e)}evaluateUnbindables(){let e=this.unbindables;for(let o=0,r=e.length;o<r;++o)e[o].unbind(this);e.length=0}};ti=To;be(zn);function mu(t){return t.create!==void 0}function bu(t,e,o,r){if(o==null&&(o=""),mu(o)){t.textContent="";let n=t.$fastView;if(n===void 0)if(vr(r)&&vr(o)&&r.bindingViewBoundaries[this.targetNodeId]!==void 0&&r.hydrationStage!==At.hydrated){let a=r.bindingViewBoundaries[this.targetNodeId];n=o.hydrate(a.first,a.last)}else n=o.create();else t.$fastTemplate!==o&&(n.isComposed&&(n.remove(),n.unbind()),n=o.create());n.isComposed?n.needsBindOnly&&(n.needsBindOnly=!1,n.bind(r.source,r.context)):(n.isComposed=!0,n.bind(r.source,r.context),n.insertBefore(t),t.$fastView=n,t.$fastTemplate=o)}else{let n=t.$fastView;if(n!==void 0&&n.isComposed&&(n.isComposed=!1,n.remove(),n.needsBindOnly?n.needsBindOnly=!1:n.unbind()),n===void 0&&vr(r)&&r.hydrationStage!==At.hydrated){let a=r.bindingViewBoundaries[this.targetNodeId];a!==void 0&&(Kt(a.first,a.last),delete r.bindingViewBoundaries[this.targetNodeId])}t.textContent=o}}function vu(t,e,o){var r;let n=`${this.id}-t`,a=(r=t[n])!==null&&r!==void 0?r:t[n]={v:0,cv:Object.create(null)},s=a.cv,d=a.v,p=t[e];if(o!=null&&o.length){let h=o.split(/\s+/);for(let u=0,v=h.length;u<v;++u){let B=h[u];B!==""&&(s[B]=d,p.add(B))}}if(a.v=d+1,d!==0){d-=1;for(let h in s)s[h]===d&&p.remove(h)}}var ku={[U.attribute]:nt.setAttribute,[U.booleanAttribute]:nt.setBooleanAttribute,[U.property]:(t,e,o)=>t[e]=o,[U.content]:bu,[U.tokenList]:vu,[U.event]:()=>{}},qe=class{constructor(e){this.updateTarget=null,this.aspectType=U.content,this.dataBinding=e}createHTML(e){return br.interpolation(e(this))}createBehavior(){var e;if(this.updateTarget===null){let o=ku[this.aspectType],r=(e=this.dataBinding.policy)!==null&&e!==void 0?e:this.policy;if(!o)throw Q.error(q.unsupportedBindingBehavior);this.data=`${this.id}-d`,this.updateTarget=r.protect(this.targetTagName,this.aspectType,this.targetAspect,o)}return this}bind(e){var o;let r=e.targets[this.targetNodeId];switch(this.aspectType){case U.event:r[this.data]=e,r.addEventListener(this.targetAspect,this,this.dataBinding.options);break;case U.content:e.onUnbind(this);default:{let n=(o=r[this.data])!==null&&o!==void 0?o:r[this.data]=this.dataBinding.createObserver(this,this);n.target=r,n.controller=e;let a=n.bind(e);if(e._skipAttrUpdates&&(this.aspectType===U.attribute||this.aspectType===U.booleanAttribute))break;this.updateTarget(r,this.targetAspect,a,e);break}}}unbind(e){let r=e.targets[this.targetNodeId].$fastView;r!==void 0&&r.isComposed&&(r.unbind(),r.needsBindOnly=!0)}handleEvent(e){let o=e.currentTarget[this.data];if(o.isBound){rt.setEvent(e);let r=this.dataBinding.evaluate(o.source,o.context);rt.setEvent(null),r!==!0&&e.preventDefault()}}handleChange(e,o){let r=o.controller;if(!r.isBound)return;let n=o.target;this.updateTarget(n,this.targetAspect,o.bind(r),r)}};ce.define(qe,{aspected:!0});var ai=(t,e)=>`${t}.${e}`,oi={},Ue={index:0,node:null};function ri(t){t.startsWith("fast-")||Q.warn(q.hostBindingWithoutHost,{name:t})}var yu=new Proxy(document.createElement("div"),{get(t,e){ri(e);let o=Reflect.get(t,e);return Ge(o)?o.bind(t):o},set(t,e,o){return ri(e),Reflect.set(t,e,o)}}),Dn=class{constructor(e,o,r){this.fragment=e,this.directives=o,this.policy=r,this.proto=null,this.nodeIds=new Set,this.descriptors={},this.factories=[]}addFactory(e,o,r,n,a){var s,d;this.nodeIds.has(r)||(this.nodeIds.add(r),this.addTargetDescriptor(o,r,n)),e.id=(s=e.id)!==null&&s!==void 0?s:Po(),e.targetNodeId=r,e.targetTagName=a,e.policy=(d=e.policy)!==null&&d!==void 0?d:this.policy,this.factories.push(e)}freeze(){return this.proto=Object.create(null,this.descriptors),this}addTargetDescriptor(e,o,r){let n=this.descriptors;if(o==="r"||o==="h"||n[o])return;if(!n[e]){let s=e.lastIndexOf("."),d=e.substring(0,s),p=parseInt(e.substring(s+1),10);this.addTargetDescriptor(d,e,p)}let a=oi[o];if(!a){let s=`_${o}`;oi[o]=a={get(){var d;return(d=this[s])!==null&&d!==void 0?d:this[s]=this[e].childNodes[r]}}}n[o]=a}createView(e){let o=this.fragment.cloneNode(!0),r=Object.create(this.proto);r.r=o,r.h=e??yu;for(let n of this.nodeIds)Reflect.get(r,n);return new Tt(o,this.factories,r)}};function ii(t,e,o,r,n,a=!1){let s=o.attributes,d=t.directives;for(let p=0,h=s.length;p<h;++p){let u=s[p],v=u.value,B=wo.parse(v,d),F=null;B===null?a&&(F=new qe(ar(()=>v,t.policy)),ce.assignAspect(F,u.name)):F=Sr.aggregate(B,t.policy),F!==null&&(o.removeAttributeNode(u),p--,h--,t.addFactory(F,e,r,n,o.tagName))}}function xu(t,e,o,r,n){let a=wo.parse(e.textContent,t.directives);if(a===null)return Ue.node=e.nextSibling,Ue.index=n+1,Ue;let s,d=s=e;for(let p=0,h=a.length;p<h;++p){let u=a[p];p!==0&&(n++,r=ai(o,n),s=d.parentNode.insertBefore(document.createTextNode(""),d.nextSibling)),me(u)?s.textContent=u:(s.textContent=" ",ce.assignAspect(u),t.addFactory(u,o,r,n,null)),d=s}return Ue.index=n+1,Ue.node=d.nextSibling,Ue}function si(t,e,o){let r=0,n=e.firstChild;for(;n;){let a=Su(t,o,n,r);n=a.node,r=a.index}}function Su(t,e,o,r){let n=ai(e,r);switch(o.nodeType){case 1:ii(t,e,o,n,r),si(t,o,n);break;case 3:return xu(t,o,e,n,r);case 8:{let a=wo.parse(o.data,t.directives);a!==null&&t.addFactory(Sr.aggregate(a),e,n,r,null);break}}return Ue.index=r+1,Ue.node=o.nextSibling,Ue}function Bu(t,e){return t&&t.nodeType===8&&wo.parse(t.data,e)!==null}var ni="TEMPLATE",Sr={compile(t,e,o=nt.policy){let r;if(me(t)){r=document.createElement(ni),r.innerHTML=o.createHTML(t);let s=r.content.firstElementChild;s!==null&&s.tagName===ni&&(r=s)}else r=t;!r.content.firstChild&&!r.content.lastChild&&r.content.appendChild(document.createComment(""));let n=document.adoptNode(r.content),a=new Dn(n,e,o);return ii(a,"",r,"h",0,!0),(Bu(n.firstChild,e)||n.childNodes.length===1&&Object.keys(e).length>0)&&n.insertBefore(document.createComment(""),n.firstChild),si(a,n,"r"),Ue.node=null,a.freeze()},setDefaultStrategy(t){this.compile=t},aggregate(t,e=nt.policy){if(t.length===1)return t[0];let o,r=!1,n,a=t.length,s=t.map(h=>me(h)?()=>h:(o=h.sourceAspect||o,r=r||h.dataBinding.isVolatile,n=n||h.dataBinding.policy,h.dataBinding.evaluate)),d=(h,u)=>{let v="";for(let B=0;B<a;++B)v+=s[B](h,u);return v},p=new qe(ir(d,n??e,r));return ce.assignAspect(p,o),p}};var Io=class extends Ft{bind(e){e.source[this.options]=e.targets[this.targetNodeId]}};ce.define(Io);var de=t=>new Io(t);var Nu=/([ \x09\x0a\x0c\x0d])([^\0-\x1F\x7F-\x9F "'>=/]+)([ \x09\x0a\x0c\x0d]*=[ \x09\x0a\x0c\x0d]*(?:[^ \x09\x0a\x0c\x0d"'`<>=]*|"[^"]*|'[^']*))$/,$u=Object.create(null),Oe=class{constructor(e,o=$u){this.html=e,this.factories=o}createHTML(e){let o=this.factories;for(let r in o)e(o[r]);return this.html}};Oe.empty=new Oe("");ce.define(Oe);function Pu(t,e,o,r=ce.getForInstance(t)){if(r.aspected){let n=Nu.exec(e);n!==null&&ce.assignAspect(t,n[2])}return t.createHTML(o)}var Ho=class t{constructor(e,o={},r){this.policy=r,this.result=null,this.html=e,this.factories=o}compile(){return this.result===null&&(this.result=Sr.compile(this.html,this.factories,this.policy)),this.result}inline(){return new Oe(me(this.html)?this.html:this.html.innerHTML,this.factories)}withPolicy(e){if(this.result)throw Q.error(q.cannotSetTemplatePolicyAfterCompilation);if(this.policy)throw Q.error(q.onlySetTemplatePolicyOnce);return this.policy=e,this}render(e,o,r){let n=this.create(r);return n.bind(e),n.appendTo(o),n}create(e){return this.compile().createView(e)}static create(e,o,r){let n="",a=Object.create(null),s=d=>{var p;let h=(p=d.id)!==null&&p!==void 0?p:d.id=Po();return a[h]=d,h};for(let d=0,p=e.length-1;d<p;++d){let h=e[d],u=o[d],v;if(n+=h,Ge(u))u=new qe(ir(u));else if(u instanceof bt)u=new qe(u);else if(!(v=ce.getForInstance(u))){let B=u;u=new qe(ar(()=>B))}n+=Pu(u,h,s,v)}return new t(n+e[e.length-1],a,r)}};be(Ho);var m=((t,...e)=>{if(Array.isArray(t)&&Array.isArray(t.raw))return Ho.create(t,e);throw Q.error(q.directCallToHTMLTagNotAllowed)});m.partial=t=>new Oe(t);var li="slotchange",zo=class extends Fo{observe(e){e.addEventListener(li,this)}disconnect(e){e.removeEventListener(li,this)}getNodes(e){return e.assignedNodes(this.options)}handleEvent(e){let o=e.currentTarget;this.updateTarget(this.getSource(o),this.computeNodes(o))}};ce.define(zo);function oe(t){return me(t)&&(t={property:t}),new zo(t)}function Ae(t){return t?typeof t=="string"?new Oe(t):"inline"in t?t.inline():t:Oe.empty}var Ce=class{};function Ke(t){return m` <slot name="end" ${de("end")}>${Ae(t.end)}</slot> `.inline()}function Ye(t){return m` <slot name="start" ${de("start")}>${Ae(t.start)}</slot> `.inline()}function Je(t,...e){let o=jt.locate(t);e.forEach(r=>{Object.getOwnPropertyNames(r.prototype).forEach(a=>{a!=="constructor"&&Object.defineProperty(t.prototype,a,Object.getOwnPropertyDescriptor(r.prototype,a))}),jt.locate(r).forEach(a=>o.push(a))})}var g=Object.freeze({prefix:"fluent",shadowRootMode:"open",registry:globalThis.customElements});function ci(t){return e=>e?.nodeType!==Node.ELEMENT_NODE?!1:e.tagName.toLowerCase().endsWith(t)}var wu=":host([hidden]){display:none}";function S(t){return`${wu}:host{display:${t}}`}var P="var(--colorNeutralForeground1)",Br="var(--colorNeutralForeground1Hover)",di="var(--colorNeutralForeground1Pressed)";var Re="var(--colorNeutralForeground2)",ui="var(--colorNeutralForeground2Hover)",pi="var(--colorNeutralForeground2Pressed)";var Ln="var(--colorNeutralForeground2BrandHover)",En="var(--colorNeutralForeground2BrandPressed)";var W="var(--colorNeutralForeground3)",hi="var(--colorNeutralForeground3Hover)",gi="var(--colorNeutralForeground3Pressed)";var Nr="var(--colorNeutralForeground4)";var z="var(--colorNeutralForegroundDisabled)",fi="var(--colorBrandForegroundLink)",mi="var(--colorBrandForegroundLinkHover)",bi="var(--colorBrandForegroundLinkPressed)";var vi="var(--colorNeutralForeground2Link)",ki="var(--colorNeutralForeground2LinkHover)",yi="var(--colorNeutralForeground2LinkPressed)";var xi="var(--colorCompoundBrandForeground1Hover)";var at="var(--colorNeutralForegroundOnBrand)",Ie="var(--colorNeutralForegroundInverted)",Si="var(--colorNeutralForegroundInvertedHover)",Bi="var(--colorNeutralForegroundInvertedPressed)";var Ni="var(--colorNeutralForegroundStaticInverted)";var Do="var(--colorBrandForeground1)",$i="var(--colorBrandForeground2)";var Pi="var(--colorNeutralForeground1Static)";var E="var(--colorNeutralBackground1)",wi="var(--colorNeutralBackground1Hover)",Fi="var(--colorNeutralBackground1Pressed)";var Ct="var(--colorNeutralBackground3)";var Ti="var(--colorNeutralBackground4)";var Ai="var(--colorNeutralBackground5)";var $r="var(--colorNeutralBackground6)";var Pr="var(--colorNeutralBackgroundInverted)";var Ci="var(--colorSubtleBackground)",wr="var(--colorSubtleBackgroundHover)",Lo="var(--colorSubtleBackgroundPressed)";var re="var(--colorTransparentBackground)",On="var(--colorTransparentBackgroundHover)",Rn="var(--colorTransparentBackgroundPressed)";var yt="var(--colorNeutralBackgroundDisabled)";var Ii="var(--colorBackgroundOverlay)";var Fr="var(--colorBrandBackground)",Hi="var(--colorBrandBackgroundHover)",zi="var(--colorBrandBackgroundPressed)";var it="var(--colorCompoundBrandBackground)",It="var(--colorCompoundBrandBackgroundHover)",Ht="var(--colorCompoundBrandBackgroundPressed)";var Di="var(--colorBrandBackground2)";var Fe="var(--colorNeutralStrokeAccessible)",Me="var(--colorNeutralStrokeAccessibleHover)",_e="var(--colorNeutralStrokeAccessiblePressed)";var Ve="var(--colorNeutralStroke1)",st="var(--colorNeutralStroke1Hover)",Yt="var(--colorNeutralStroke1Pressed)";var Jt="var(--colorNeutralStroke2)",Li="var(--colorNeutralStroke3)";var Ei="var(--colorNeutralStrokeOnBrand2)";var Tr="var(--colorBrandStroke1)",Ar="var(--colorBrandStroke2)";var He="var(--colorCompoundBrandStroke)",Oi="var(--colorCompoundBrandStrokeHover)",Cr="var(--colorCompoundBrandStrokePressed)",Z="var(--colorNeutralStrokeDisabled)";var K="var(--colorTransparentStroke)",Mn="var(--colorTransparentStrokeInteractive)";var Ri="var(--colorStrokeFocus1)",ue="var(--colorStrokeFocus2)",Mi="var(--colorNeutralShadowAmbient)",_i="var(--colorNeutralShadowKey)";var Ir="var(--colorPaletteRedBackground1)";var Hr="var(--colorPaletteRedBackground3)";var zr="var(--colorPaletteRedBorder1)",Eo="var(--colorPaletteRedBorder2)",Qt="var(--colorPaletteRedForeground1)";var _n="var(--colorPaletteRedForeground3)";var Dr="var(--colorPaletteGreenBackground1)";var Lr="var(--colorPaletteGreenBackground3)";var Vi="var(--colorPaletteGreenBorder1)",Wi="var(--colorPaletteGreenBorder2)",ji="var(--colorPaletteGreenForeground1)",Gi="var(--colorPaletteGreenForeground2)",Xi="var(--colorPaletteGreenForeground3)";var Er="var(--colorPaletteDarkOrangeBackground1)";var Or="var(--colorPaletteDarkOrangeBackground3)";var Rr="var(--colorPaletteDarkOrangeBorder1)";var qi="var(--colorPaletteDarkOrangeForeground1)";var Vn="var(--colorPaletteDarkOrangeForeground3)",Ui="var(--colorPaletteYellowBackground1)";var Ki="var(--colorPaletteYellowBackground3)";var Yi="var(--colorPaletteYellowBorder1)";var Mr="var(--colorPaletteYellowForeground2)";var Zt="var(--borderRadiusNone)",lt="var(--borderRadiusSmall)",A="var(--borderRadiusMedium)",Ji="var(--borderRadiusLarge)",Qi="var(--borderRadiusXLarge)";var ee="var(--borderRadiusCircular)",C="var(--fontFamilyBase)",Zi="var(--fontFamilyMonospace)",es="var(--fontFamilyNumeric)",_r="var(--fontSizeBase100)",R="var(--fontSizeBase200)",j="var(--fontSizeBase300)",pe="var(--fontSizeBase400)",eo="var(--fontSizeBase500)",Vr="var(--fontSizeBase600)",ts="var(--fontSizeHero700)",os="var(--fontSizeHero800)",rs="var(--fontSizeHero900)",ns="var(--fontSizeHero1000)",_="var(--fontWeightRegular)",as="var(--fontWeightMedium)",he="var(--fontWeightSemibold)",is="var(--fontWeightBold)",Wr="var(--lineHeightBase100)",G="var(--lineHeightBase200)",Y="var(--lineHeightBase300)",xe="var(--lineHeightBase400)",jr="var(--lineHeightBase500)",ss="var(--lineHeightBase600)",ls="var(--lineHeightHero700)",cs="var(--lineHeightHero800)",ds="var(--lineHeightHero900)",us="var(--lineHeightHero1000)",to="var(--shadow2)",Gr="var(--shadow4)";var ps="var(--shadow64)";var M="var(--strokeWidthThin)",Se="var(--strokeWidthThick)",ze="var(--strokeWidthThicker)",hs="var(--strokeWidthThickest)";var ne="var(--spacingHorizontalXXS)",ae="var(--spacingHorizontalXS)",We="var(--spacingHorizontalSNudge)",Qe="var(--spacingHorizontalS)",Te="var(--spacingHorizontalMNudge)",ie="var(--spacingHorizontalM)",gs="var(--spacingHorizontalL)";var fs="var(--spacingHorizontalXXL)";var Oo="var(--spacingVerticalXXS)",zt="var(--spacingVerticalXS)",Ro="var(--spacingVerticalSNudge)",te="var(--spacingVerticalS)",oo="var(--spacingVerticalMNudge)",ms="var(--spacingVerticalM)",ro="var(--spacingVerticalL)";var no="var(--spacingVerticalXXL)";var Xr="var(--durationUltraFast)",bs="var(--durationFaster)";var ao="var(--durationNormal)",vs="var(--durationGentle)",ks="var(--durationSlow)";var io="var(--curveAccelerateMid)";var ys="var(--curveDecelerateMax)",so="var(--curveDecelerateMid)";var lo="var(--curveEasyEase)",xs="var(--curveLinear)";function Ss(t,e){if("requestIdleCallback"in globalThis)return globalThis.requestIdleCallback(t,e);let o=Date.now();return setTimeout(()=>{t({didTimeout:e?.timeout?Date.now()-o>=e.timeout:!1,timeRemaining:()=>0})},e?.timeout??1)}function Bs(t,e,o){let r=o?.shallow??!1,n=o?.timeout??50,a=o?.idleCallback??!1,s=`${r?":scope > ":""}:not(:defined)`,d=p=>{if(t.querySelector(s)===null||p&&p.timeRemaining()<=0){a?Ss(e,{timeout:n}):e();return}Ss(d,{timeout:n})};d()}var Ns={primary:"primary",outline:"outline",subtle:"subtle",transparent:"transparent"},$s={circular:"circular",rounded:"rounded",square:"square"},Ps={small:"small",medium:"medium",large:"large"},Mo={submit:"submit",reset:"reset",button:"button"};var ws=`${g.prefix}-button`;var Fs=Ns,Ts=$s,As=Ps;var Cs={download:"download",href:"href",hreflang:"hreflang",ping:"ping",referrerpolicy:"referrerpolicy",rel:"rel",target:"target",type:"type"},Is=`${g.prefix}-anchor-button`;var Hs=`${g.prefix}-link`;var zs=f`
  ${S("inline")}

  :host {
    position: relative;
    box-sizing: border-box;
    background-color: transparent;
    color: ${fi};
    cursor: pointer;
    font-family: ${C};
    font-size: ${j};
    font-weight: ${_};
    overflow: inherit;
    text-align: start;
    text-decoration: none;
    text-decoration-thickness: ${M};
    text-overflow: inherit;
    user-select: text;
  }

  :host(:hover) {
    outline: none;
    text-decoration-line: underline;
  }

  @media (hover: hover) {
    :host(:hover) {
      color: ${mi};
    }

    :host(:active) {
      color: ${bi};
    }

    :host([appearance='subtle']:hover) {
      color: ${ki};
    }

    :host([appearance='subtle']:active) {
      color: ${yi};
    }
  }

  :host([appearance='subtle']) {
    color: ${vi};
  }

  :host-context(:is(h1, h2, h3, h4, h5, h6, p, fluent-text)),
  :host([inline]) {
    font: inherit;
    text-decoration: underline;
  }

  :host(:focus-visible),
  :host-context(:is(h1, h2, h3, h4, h5, h6, p, fluent-text)):focus-visible,
  :host([inline]:focus-visible) {
    outline-style: none;
    text-decoration-line: underline;
    text-decoration-style: double;
    text-decoration-color: ${ue};
  }

  :host(:not([href])) {
    color: inherit;
    text-decoration: none;
  }

  ::slotted(a) {
    position: absolute;
    inset: 0;
  }

  @media (forced-colors: active) {
    :host {
      color: LinkText;
    }
  }
`;function Fu(){return m`
    <template
      tabindex="0"
      @click="${(t,e)=>t.clickHandler(e.event)}"
      @keydown="${(t,e)=>t.keydownHandler(e.event)}"
    >
      <slot></slot>
    </template>
  `}var Ds=Fu();var Ls={name:Hs,registry:g.registry,styles:zs,template:Ds};var Be=class extends y{constructor(){super(),this.isMac=navigator.userAgent.includes("Mac"),this.elementInternals=this.attachInternals(),this.internalProxyAnchor=this.createProxyElement(),this.elementInternals.role="link"}connectedCallback(){super.connectedCallback(),this.tabIndex=Number(this.getAttribute("tabindex")??0)<0?-1:0,k.getNotifier(this).subscribe(this),Object.keys(this.$fastController.definition.attributeLookup).forEach(e=>{this.handleChange(this,e)}),this.append(this.internalProxyAnchor)}disconnectedCallback(){super.disconnectedCallback(),k.getNotifier(this).unsubscribe(this)}handleChange(e,o){if(o in Cs){let r=this.$fastController.definition.attributeLookup[o]?.attribute;r&&this.handleProxyAttributeChange(r,this[o])}}clickHandler(e){if(e.composedPath()[0]===this.internalProxyAnchor)return e.stopImmediatePropagation(),!0;if(this.href){let o=e.ctrlKey||e.metaKey;this.handleNavigation(o)}return!0}keydownHandler(e){if(this.href&&e.key==="Enter"){let o=this.isMac&&e.metaKey||e.ctrlKey;this.handleNavigation(o);return}return!0}handleNavigation(e){e?window.open(this.href,"_blank"):this.internalProxyAnchor.click()}handleProxyAttributeChange(e,o){o?this.internalProxyAnchor.setAttribute(e,o):this.internalProxyAnchor.removeAttribute(e)}createProxyElement(){let e=this.internalProxyAnchor??document.createElement("a");return e.inert=!0,e}};i([l],Be.prototype,"download",void 0);i([l],Be.prototype,"href",void 0);i([l],Be.prototype,"hreflang",void 0);i([l],Be.prototype,"ping",void 0);i([l],Be.prototype,"referrerpolicy",void 0);i([l],Be.prototype,"rel",void 0);i([l],Be.prototype,"target",void 0);i([l],Be.prototype,"type",void 0);var co=class extends Be{constructor(){super(...arguments),this.inline=!1}};i([l],co.prototype,"appearance",void 0);i([l({mode:"boolean"})],co.prototype,"inline",void 0);var Es={filled:"filled",ghost:"ghost",outline:"outline",tint:"tint"},Os={brand:"brand",danger:"danger",important:"important",informative:"informative",severe:"severe",subtle:"subtle",success:"success",warning:"warning"};var Rs=`${g.prefix}-badge`;var Ms=f.partial`
  ${S("inline-flex")} :host {
    position: relative;
    box-sizing: border-box;
    align-items: center;
    justify-content: center;
    font-family: ${C};
    font-weight: ${he};
    font-size: ${R};
    line-height: ${G};
    min-width: 20px;
    height: 20px;
    padding-inline: calc(${ae} + ${ne});
    border-radius: ${ee};
    border-color: ${K};
    background-color: ${Fr};
    color: ${at};
    contain: content;
  }

  ::slotted(svg) {
    font-size: 12px;
  }

  :host(:not([appearance='ghost']))::after {
    position: absolute;
    content: '';
    top: 0;
    left: 0;
    bottom: 0;
    right: 0;
    border-style: solid;
    border-width: ${M};
    border-color: inherit;
    border-radius: inherit;
  }
`,_s=f.partial`
  :host([size='tiny']) {
    width: 6px;
    height: 6px;
    font-size: 4px;
    line-height: 4px;
    padding-inline: 0;
    min-width: unset;
  }
  :host([size='tiny']) ::slotted(svg) {
    font-size: 6px;
  }
  :host([size='extra-small']) {
    width: 10px;
    height: 10px;
    font-size: 6px;
    line-height: 6px;
    padding-inline: 0;
    min-width: unset;
  }
  :host([size='extra-small']) ::slotted(svg) {
    font-size: 10px;
  }
  :host([size='small']) {
    min-width: 16px;
    height: 16px;
    font-size: ${_r};
    line-height: ${Wr};
    padding-inline: calc(${ne} + ${ne});
  }
  :host([size='small']) ::slotted(svg) {
    font-size: 12px;
  }
  :host([size='large']) {
    min-width: 24px;
    height: 24px;
    font-size: ${R};
    line-height: ${G};
    padding-inline: calc(${ae} + ${ne});
  }
  :host([size='large']) ::slotted(svg) {
    font-size: 16px;
  }
  :host([size='extra-large']) {
    min-width: 32px;
    height: 32px;
    font-size: ${R};
    line-height: ${G};
    padding-inline: calc(${We} + ${ne});
  }
  :host([size='extra-large']) ::slotted(svg) {
    font-size: 20px;
  }
`,Vs=f.partial`
  :host([color='danger']) {
    background-color: ${Hr};
    color: ${at};
  }

  :host([color='important']) {
    background-color: ${P};
    color: ${E};
  }

  :host([color='informative']) {
    background-color: ${Ai};
    color: ${W};
  }

  :host([color='severe']) {
    background-color: ${Or};
    color: ${at};
  }

  :host([color='subtle']) {
    background-color: ${E};
    color: ${P};
  }

  :host([color='success']) {
    background-color: ${Lr};
    color: ${at};
  }

  :host([color='warning']) {
    background-color: ${Ki};
    color: ${Pi};
  }
`,Ws=f.partial`
  :host([appearance='ghost']) {
    color: ${Do};
    background-color: initial;
  }

  :host([appearance='ghost'][color='danger']) {
    color: ${_n};
  }

  :host([appearance='ghost'][color='important']) {
    color: ${P};
  }

  :host([appearance='ghost'][color='informative']) {
    color: ${W};
  }

  :host([appearance='ghost'][color='severe']) {
    color: ${Vn};
  }

  :host([appearance='ghost'][color='subtle']) {
    color: ${Ie};
  }

  :host([appearance='ghost'][color='success']) {
    color: ${Xi};
  }

  :host([appearance='ghost'][color='warning']) {
    color: ${Mr};
  }
`,js=f.partial`
  :host([appearance='outline']) {
    border-color: currentColor;
    color: ${Do};
    background-color: initial;
  }

  :host([appearance='outline'][color='danger']) {
    color: ${_n};
  }

  :host([appearance='outline'][color='important']) {
    color: ${W};
    border-color: ${Fe};
  }

  :host([appearance='outline'][color='informative']) {
    color: ${W};
    border-color: ${Jt};
  }

  :host([appearance='outline'][color='severe']) {
    color: ${Vn};
  }

  :host([appearance='outline'][color='subtle']) {
    color: ${Ni};
  }

  :host([appearance='outline'][color='success']) {
    color: ${Gi};
  }

  :host([appearance='outline'][color='warning']) {
    color: ${Mr};
  }
`,Gs=f.partial`
  :host([appearance='tint']) {
    background-color: ${Di};
    color: ${$i};
    border-color: ${Ar};
  }

  :host([appearance='tint'][color='danger']) {
    background-color: ${Ir};
    color: ${Qt};
    border-color: ${zr};
  }

  :host([appearance='tint'][color='important']) {
    background-color: ${W};
    color: ${E};
    border-color: ${K};
  }

  :host([appearance='tint'][color='informative']) {
    background-color: ${Ti};
    color: ${W};
    border-color: ${Jt};
  }

  :host([appearance='tint'][color='severe']) {
    background-color: ${Er};
    color: ${qi};
    border-color: ${Rr};
  }

  :host([appearance='tint'][color='subtle']) {
    background-color: ${E};
    color: ${W};
    border-color: ${Jt};
  }

  :host([appearance='tint'][color='success']) {
    background-color: ${Dr};
    color: ${ji};
    border-color: ${Wi};
  }

  :host([appearance='tint'][color='warning']) {
    background-color: ${Ui};
    color: ${Mr};
    border-color: ${Yi};
  }
`;var Xs=f`
  :host([shape='square']) {
    border-radius: ${Zt};
  }

  :host([shape='rounded']) {
    border-radius: ${A};
  }

  :host([shape='rounded']:is([size='tiny'], [size='extra-small'], [size='small'])) {
    border-radius: ${lt};
  }

  ${Gs}
  ${js}
  ${Ws}
  ${Vs}
  ${_s}
  ${Ms}

  @media (forced-colors: active) {
    :host,
    :host([appearance='outline']),
    :host([appearance='tint']) {
      border-color: CanvasText;
    }
  }
`;function Tu(t={}){return m`
    ${Ye(t)}
    <slot>${Ae(t.defaultContent)}</slot>
    ${Ke(t)}
  `}var qs=Tu();var Us={name:Rs,registry:g.registry,styles:Xs,template:qs};var ct=class extends y{constructor(){super(...arguments),this.appearance=Es.filled,this.color=Os.brand}};i([l],ct.prototype,"appearance",void 0);i([l],ct.prototype,"color",void 0);i([l],ct.prototype,"shape",void 0);i([l],ct.prototype,"size",void 0);Je(ct,Ce);var Wn=f`
  ${S("inline-flex")}

  :host {
    --icon-spacing: ${We};
    position: relative;
    contain: layout style;
    vertical-align: middle;
    align-items: center;
    box-sizing: border-box;
    justify-content: center;
    text-align: center;
    text-decoration-line: none;
    margin: 0;
    min-height: 32px;
    outline-style: none;
    background-color: ${E};
    color: ${P};
    border: ${M} solid ${Ve};
    padding: 0 ${ie};
    min-width: 96px;
    border-radius: ${A};
    font-size: ${j};
    font-family: ${C};
    font-weight: ${he};
    line-height: ${Y};
    transition-duration: ${bs};
    transition-property: background, border, color;
    transition-timing-function: ${lo};
    cursor: pointer;
    user-select: none;
  }

  .content {
    display: inherit;
  }

  :host(:hover) {
    background-color: ${wi};
    color: ${Br};
    border-color: ${st};
  }

  :host(:hover:active) {
    background-color: ${Fi};
    border-color: ${Yt};
    color: ${di};
    outline-style: none;
  }

  :host(:focus-visible) {
    border-color: ${K};
    outline: ${Se} solid ${K};
    box-shadow: ${Gr}, 0 0 0 2px ${ue};
  }

  @media screen and (prefers-reduced-motion: reduce) {
    :host {
      transition-duration: 0.01ms;
    }
  }

  ::slotted(svg) {
    font-size: 20px;
    height: 20px;
    width: 20px;
    fill: currentColor;
  }

  ::slotted([slot='start']) {
    margin-inline-end: var(--icon-spacing);
  }

  ::slotted([slot='end']),
  [slot='end'] {
    flex-shrink: 0;
    margin-inline-start: var(--icon-spacing);
  }

  :host([icon-only]) {
    min-width: 32px;
    max-width: 32px;
  }

  :host([size='small']) {
    --icon-spacing: ${ae};
    min-height: 24px;
    min-width: 64px;
    padding: 0 ${Qe};
    border-radius: ${lt};
    font-size: ${R};
    line-height: ${G};
    font-weight: ${_};
  }

  :host([size='small'][icon-only]) {
    min-width: 24px;
    max-width: 24px;
  }

  :host([size='large']) {
    min-height: 40px;
    border-radius: ${Ji};
    padding: 0 ${gs};
    font-size: ${pe};
    line-height: ${xe};
  }

  :host([size='large'][icon-only]) {
    min-width: 40px;
    max-width: 40px;
  }

  :host([size='large']) ::slotted(svg) {
    font-size: 24px;
    height: 24px;
    width: 24px;
  }

  :host(:is([shape='circular'], [shape='circular']:focus-visible)) {
    border-radius: ${ee};
  }

  :host(:is([shape='square'], [shape='square']:focus-visible)) {
    border-radius: ${Zt};
  }

  :host([appearance='primary']) {
    background-color: ${Fr};
    color: ${at};
    border-color: transparent;
  }

  :host([appearance='primary']:hover) {
    background-color: ${Hi};
  }

  :host([appearance='primary']:is(:hover, :hover:active):not(:focus-visible)) {
    border-color: transparent;
  }

  :host([appearance='primary']:is(:hover, :hover:active)) {
    color: ${at};
  }

  :host([appearance='primary']:hover:active) {
    background-color: ${zi};
  }

  :host([appearance='primary']:focus-visible) {
    border-color: ${at};
    box-shadow: ${to}, 0 0 0 2px ${ue};
  }

  :host([appearance='outline']) {
    background-color: ${re};
  }

  :host([appearance='outline']:hover) {
    background-color: ${On};
  }

  :host([appearance='outline']:hover:active) {
    background-color: ${Rn};
  }

  :host([appearance='subtle']) {
    background-color: ${Ci};
    color: ${Re};
    border-color: transparent;
  }

  :host([appearance='subtle']:hover) {
    background-color: ${wr};
    color: ${ui};
    border-color: transparent;
  }

  :host([appearance='subtle']:hover:active) {
    background-color: ${Lo};
    color: ${pi};
    border-color: transparent;
  }

  :host([appearance='subtle']:hover) ::slotted(svg) {
    fill: ${Ln};
  }

  :host([appearance='subtle']:hover:active) ::slotted(svg) {
    fill: ${En};
  }

  :host([appearance='transparent']) {
    background-color: ${re};
    color: ${Re};
  }

  :host([appearance='transparent']:hover) {
    background-color: ${On};
    color: ${Ln};
  }

  :host([appearance='transparent']:hover:active) {
    background-color: ${Rn};
    color: ${En};
  }

  :host(:is([appearance='transparent'], [appearance='transparent']:is(:hover, :active))) {
    border-color: transparent;
  }
`,Ks=f`
  ${Wn}

  :host(:is(:disabled, [disabled], [disabled-focusable], [appearance]:disabled, [appearance][disabled], [appearance][disabled-focusable])),
  :host(:is(:disabled, [disabled], [disabled-focusable], [appearance]:disabled, [appearance][disabled], [appearance][disabled-focusable]):hover),
  :host(:is(:disabled, [disabled], [disabled-focusable], [appearance]:disabled, [appearance][disabled], [appearance][disabled-focusable]):hover:active) {
    background-color: ${yt};
    border-color: ${Z};
    color: ${z};
    cursor: not-allowed;
  }

  :host([appearance='primary']:is(:disabled, [disabled], [disabled-focusable])),
  :host([appearance='primary']:is(:disabled, [disabled], [disabled-focusable]):is(:hover, :hover:active)) {
    border-color: transparent;
  }

  :host([appearance='outline']:is(:disabled, [disabled], [disabled-focusable])),
  :host([appearance='outline']:is(:disabled, [disabled], [disabled-focusable]):is(:hover, :hover:active)) {
    background-color: ${re};
  }

  :host([appearance='subtle']:is(:disabled, [disabled], [disabled-focusable])),
  :host([appearance='subtle']:is(:disabled, [disabled], [disabled-focusable]):is(:hover, :hover:active)) {
    background-color: ${re};
    border-color: transparent;
  }

  :host([appearance='transparent']:is(:disabled, [disabled], [disabled-focusable])),
  :host([appearance='transparent']:is(:disabled, [disabled], [disabled-focusable]):is(:hover, :hover:active)) {
    border-color: transparent;
    background-color: ${re};
  }

  @media (forced-colors: active) {
    :host {
      background-color: ButtonFace;
      color: ButtonText;
    }

    :host(:is(:hover, :focus-visible)) {
      border-color: Highlight !important;
    }

    :host([appearance='primary']:not(:is(:hover, :focus-visible))) {
      background-color: Highlight;
      color: HighlightText;
      forced-color-adjust: none;
    }

    :host(
        :is(
            :disabled,
            [disabled],
            [disabled-focusable],
            [appearance]:disabled,
            [appearance][disabled],
            [appearance][disabled-focusable]
          )
      ) {
      background-color: ButtonFace;
      color: GrayText;
      border-color: ButtonText;
    }
  }
`;function Au(t={}){return m`
    <template
      @click="${(e,o)=>e.clickHandler(o.event)}"
      @keypress="${(e,o)=>e.keypressHandler(o.event)}"
    >
      ${Ye(t)}
      <span class="content" part="content">
        <slot ${oe("defaultSlottedContent")}></slot>
      </span>
      ${Ke(t)}
    </template>
  `}var Ys=Au();var Js={name:ws,registry:g.registry,styles:Ks,template:Ys};function Ze(t){let e=t.ownerDocument;t?.isConnected&&t?.hasAttribute("autofocus")&&t?.checkVisibility?.({contentVisibilityAuto:!0,visibilityProperty:!0})&&[null,t,e.body,e.documentElement].includes(e.activeElement)&&O.enqueue(()=>{t.focus()})}var se=class extends y{disabledChanged(){this.setTabIndex()}disabledFocusableChanged(e,o){this.elementInternals&&(this.elementInternals.ariaDisabled=`${!!o}`)}get form(){return this.elementInternals.form}static{this.formAssociated=!0}get labels(){return Object.freeze(Array.from(this.elementInternals.labels))}typeChanged(e,o){o!==Mo.submit&&(this.formSubmissionFallbackControl?.remove(),this.shadowRoot?.querySelector('slot[name="internal"]')?.remove())}clickHandler(e){if(e&&this.disabledFocusable){e.stopImmediatePropagation();return}return this.press(),!0}connectedCallback(){super.connectedCallback(),this.elementInternals.ariaDisabled=`${!!this.disabledFocusable}`,this.setTabIndex(),Ze(this)}constructor(){super(),this.disabledFocusable=!1,this.elementInternals=this.attachInternals(),this.elementInternals.role="button"}createAndInsertFormSubmissionFallbackControl(){let e=this.formSubmissionFallbackControlSlot??document.createElement("slot");e.setAttribute("name","internal"),this.shadowRoot?.appendChild(e),this.formSubmissionFallbackControlSlot=e;let o=this.formSubmissionFallbackControl??document.createElement("button");o.style.display="none",o.setAttribute("type","submit"),o.setAttribute("slot","internal"),this.formNoValidate&&o.toggleAttribute("formnovalidate",!0),this.elementInternals.form?.id&&o.setAttribute("form",this.elementInternals.form.id),this.name&&o.setAttribute("name",this.name),this.value&&o.setAttribute("value",this.value),this.formAction&&o.setAttribute("formaction",this.formAction??""),this.formEnctype&&o.setAttribute("formenctype",this.formEnctype??""),this.formMethod&&o.setAttribute("formmethod",this.formMethod??""),this.formTarget&&o.setAttribute("formtarget",this.formTarget??""),this.append(o),this.formSubmissionFallbackControl=o}formDisabledCallback(e){this.disabled=e}keypressHandler(e){if(e&&this.disabledFocusable){e.stopImmediatePropagation();return}if(e.key==="Enter"||e.key===" "){this.click();return}return!0}press(){switch(this.type){case Mo.reset:{this.resetForm();break}case Mo.submit:{this.submitForm();break}}}resetForm(){this.elementInternals.form?.reset()}setTabIndex(){if(this.disabled){this.removeAttribute("tabindex");return}this.tabIndex=Number(this.getAttribute("tabindex")??0)<0?-1:0}submitForm(){if(!(!this.elementInternals.form||this.disabled||this.type!==Mo.submit)){if(!this.name&&!this.formAction&&!this.formEnctype&&!this.formAttribute&&!this.formMethod&&!this.formNoValidate&&!this.formTarget){this.elementInternals.form.requestSubmit();return}try{this.elementInternals.setFormValue(this.value??""),this.elementInternals.form.requestSubmit(this)}catch{this.createAndInsertFormSubmissionFallbackControl(),this.elementInternals.setFormValue(null),this.elementInternals.form.requestSubmit(this.formSubmissionFallbackControl)}}}};i([T],se.prototype,"defaultSlottedContent",void 0);i([l({mode:"boolean"})],se.prototype,"disabled",void 0);i([l({attribute:"disabled-focusable",mode:"boolean"})],se.prototype,"disabledFocusable",void 0);i([l({attribute:"formaction"})],se.prototype,"formAction",void 0);i([l({attribute:"form"})],se.prototype,"formAttribute",void 0);i([l({attribute:"formenctype"})],se.prototype,"formEnctype",void 0);i([l({attribute:"formmethod"})],se.prototype,"formMethod",void 0);i([l({attribute:"formnovalidate",mode:"boolean"})],se.prototype,"formNoValidate",void 0);i([l({attribute:"formtarget"})],se.prototype,"formTarget",void 0);i([l],se.prototype,"name",void 0);i([l],se.prototype,"type",void 0);i([l],se.prototype,"value",void 0);var dt=class extends se{constructor(){super(...arguments),this.iconOnly=!1}};i([l],dt.prototype,"appearance",void 0);i([l],dt.prototype,"shape",void 0);i([l],dt.prototype,"size",void 0);i([l({attribute:"icon-only",mode:"boolean"})],dt.prototype,"iconOnly",void 0);Je(dt,Ce);var Qs=`${g.prefix}-checkbox`;var jn=CSS.supports("anchor-name: --a"),Zs="anchor"in HTMLElement.prototype,Gn=CSS.supports("selector(:state(g))");var el=new Map;function I(t){return el.get(t)??el.set(t,Gn?`:state(${t})`:`[state--${t}]`).get(t)}function D(t,e,o){if(!(!e||!t)){if(!Gn){t.shadowRoot.host.toggleAttribute(`state--${e}`,o);return}if(o??!t.states.has(e)){t.states.add(e);return}t.states.delete(e)}}var Xn=new WeakMap;function qn(t,e){if(!t||!e)return!1;if(Xn.has(t))return Xn.get(t).has(e);let o=new Set(Object.values(t));return Xn.set(t,o),o.has(e)}function De(t,e="",o="",r,n=""){D(t,`${n}${e}`,!1),(!r||qn(r,o))&&D(t,`${n}${o}`,!0)}var Om=I("active"),tl=I("bad-input"),x=I("checked"),ol=I("custom-error"),Rm=I("description"),je=I("disabled"),N=":is([disabled], :disabled)",Mm=I("error"),_m=I("flip-block"),rl=I("focus-visible"),Vm=I("has-message"),xt=I("indeterminate"),Wm=I("multiple"),jm=I("open"),nl=I("pattern-mismatch"),Gm=I("placeholder-shown"),Xm=I("pressed"),al=I("range-overflow"),il=I("range-underflow"),sl=I("required"),qm=I("selected"),ll=I("step-mismatch"),Um=I("submenu"),cl=I("too-long"),dl=I("too-short"),ul=I("type-mismatch"),pl=I("user-invalid"),hl=I("valid"),gl=I("value-missing");var fl=f`
  ${S("inline-flex")}

  :host {
    --size: 16px;
    background-color: ${E};
    border-radius: ${lt};
    border: ${M} solid ${Fe};
    box-sizing: border-box;
    cursor: pointer;
    position: relative;
    width: var(--size);
  }

  :host,
  .indeterminate-indicator,
  .checked-indicator {
    aspect-ratio: 1;
  }

  :host(:hover) {
    border-color: ${Me};
  }

  :host(:active) {
    border-color: ${_e};
  }

  :host(${x}:hover) {
    background-color: ${It};
    border-color: ${Oi};
  }

  :host(${x}:active) {
    background-color: ${Ht};
    border-color: ${Cr};
  }

  :host(:focus-visible) {
    outline: none;
  }

  :host(:not([slot='input']))::after {
    content: '';
    position: absolute;
    inset: -8px;
    box-sizing: border-box;
    outline: none;
    border: ${Se} solid ${K};
    border-radius: ${A};
  }

  :host(:not([slot='input']):focus-visible)::after {
    border-color: ${ue};
  }

  .indeterminate-indicator,
  .checked-indicator {
    color: ${Ie};
    inset: 0;
    margin: auto;
    position: absolute;
  }

  ::slotted([slot='checked-indicator']),
  .checked-indicator {
    fill: currentColor;
    display: inline-flex;
    flex: 1 0 auto;
    width: 12px;
  }

  :host(:not(${x})) *:is(::slotted([slot='checked-indicator']), .checked-indicator) {
    display: none;
  }

  :host(${x}),
  :host(${xt}) {
    border-color: ${He};
  }

  :host(${x}),
  :host(${xt}) .indeterminate-indicator {
    background-color: ${it};
  }

  :host(${xt}) .indeterminate-indicator {
    border-radius: ${lt};
    position: absolute;
    width: calc(var(--size) / 2);
    inset: 0;
  }

  :host([size='large']) {
    --size: 20px;
  }

  :host([size='large']) ::slotted([slot='checked-indicator']),
  :host([size='large']) .checked-indicator {
    width: 16px;
  }

  :host([shape='circular']),
  :host([shape='circular']) .indeterminate-indicator {
    border-radius: ${ee};
  }

  :host(${N}),
  :host(${N}${x}) {
    background-color: ${yt};
    border-color: ${Z};
  }

  :host(${N}) {
    cursor: unset;
  }

  :host(${N}${xt}) .indeterminate-indicator {
    background-color: ${Z};
  }

  :host(${N}${x}) .checked-indicator {
    color: ${Z};
  }

  @media (forced-colors: active) {
    :host {
      border-color: FieldText;
    }

    :host(:not([slot='input']:focus-visible))::after {
      border-color: Canvas;
    }

    :host(:not(${N}):hover),
    :host(${x}:not(${N}):hover),
    :host(:not([slot='input']):focus-visible)::after {
      border-color: Highlight;
    }

    .indeterminate-indicator,
    .checked-indicator {
      color: HighlightText;
    }

    :host(${x}),
    :host(${xt}) .indeterminate-indicator {
      background-color: FieldText;
    }

    :host(${x}:not(${N}):hover),
    :host(${xt}:not(${N}):hover) .indeterminate-indicator {
      background-color: Highlight;
    }

    :host(${N}) {
      border-color: GrayText;
    }

    :host(${N}${xt}) .indeterminate-indicator {
      background-color: GrayText;
    }

    :host(${N}),
    :host(${N}${x}) .checked-indicator {
      color: GrayText;
    }
  }
`;var Cu=m.partial(`
    <svg
        fill="currentColor"
        aria-hidden="true"
        class="checked-indicator"
        width="1em"
        height="1em"
        viewBox="0 0 12 12"
        xmlns="http://www.w3.org/2000/svg">
            <path d="M9.76 3.2c.3.29.32.76.04 1.06l-4.25 4.5a.75.75 0 0 1-1.08.02L2.22 6.53a.75.75 0 0 1 1.06-1.06l1.7 1.7L8.7 3.24a.75.75 0 0 1 1.06-.04Z" fill="currentColor"></path>
    </svg>
`),Iu=m.partial(`
    <span class="indeterminate-indicator"></span>
`);function Hu(t={}){return m`
    <template
      @click="${(e,o)=>e.clickHandler(o.event)}"
      @input="${(e,o)=>e.inputHandler(o.event)}"
      @keydown="${(e,o)=>e.keydownHandler(o.event)}"
      @keyup="${(e,o)=>e.keyupHandler(o.event)}"
    >
      <slot name="checked-indicator">${Ae(t.checkedIndicator)}</slot>
      <slot name="indeterminate-indicator">${Ae(t.indeterminateIndicator)}</slot>
    </template>
  `}var ml=Hu({checkedIndicator:Cu,indeterminateIndicator:Iu});var bl={name:Qs,registry:g.registry,styles:fl,template:ml};var ge=class extends y{constructor(){super(...arguments),this.initialValue="on",this._keydownPressed=!1,this.dirtyChecked=!1,this.elementInternals=this.attachInternals(),this._validationFallbackMessage="",this._value=this.initialValue}get checked(){return k.track(this,"checked"),!!this._checked}set checked(e){this._checked=e,this.setFormValue(e?this.value:null),this.setValidity(),this.setAriaChecked(),D(this.elementInternals,"checked",e),k.notify(this,"checked")}disabledChanged(e,o){this.disabled?this.removeAttribute("tabindex"):this.tabIndex=Number(this.getAttribute("tabindex")??0)<0?-1:0,this.elementInternals.ariaDisabled=this.disabled?"true":"false",D(this.elementInternals,"disabled",this.disabled)}disabledAttributeChanged(e,o){this.disabled=!!o}initialCheckedChanged(e,o){this.dirtyChecked||(this.checked=!!o)}initialValueChanged(e,o){this._value=o}requiredChanged(e,o){this.elementInternals&&(this.setValidity(),this.elementInternals.ariaRequired=this.required?"true":"false")}get form(){return this.elementInternals.form}static{this.formAssociated=!0}get labels(){return Object.freeze(Array.from(this.elementInternals.labels))}get validationMessage(){if(this.elementInternals?.validationMessage)return this.elementInternals.validationMessage;if(!this._validationFallbackMessage){let e=document.createElement("input");e.type="checkbox",e.required=!0,e.checked=!1,this._validationFallbackMessage=e.validationMessage}return this._validationFallbackMessage}get validity(){return this.elementInternals.validity}get value(){return k.track(this,"value"),this._value}set value(e){this._value=e,this.elementInternals&&(this.setFormValue(e),this.setValidity()),k.notify(this,"value")}get willValidate(){return this.elementInternals.willValidate}checkValidity(){return this.elementInternals.checkValidity()}clickHandler(e){if(this.disabled)return;this.dirtyChecked=!0;let o=this.checked;return this.toggleChecked(),o!==this.checked&&(this.$emit("change"),this.$emit("input")),!0}connectedCallback(){super.connectedCallback(),this.disabled=!!this.disabledAttribute,this.setAriaChecked(),this.setValidity(),Ze(this)}inputHandler(e){return this.setFormValue(this.value),this.setValidity(),!0}keydownHandler(e){if(e.key!==" ")return!0;this._keydownPressed=!0}keyupHandler(e){if(!this._keydownPressed||e.key!==" ")return!0;this._keydownPressed=!1,this.click()}formResetCallback(){this.checked=this.initialChecked??!1,this.dirtyChecked=!1,this.setValidity()}reportValidity(){return this.elementInternals.reportValidity()}setAriaChecked(e=this.checked){this.elementInternals&&(this.elementInternals.ariaChecked=e?"true":"false")}setFormValue(e,o){this.elementInternals?.setFormValue(e,e??o)}setCustomValidity(e){this.elementInternals.setValidity({customError:!0},e),this.setValidity()}setValidity(e,o,r){if(this.elementInternals){if(this.disabled||!this.required){this.elementInternals.setValidity({});return}this.elementInternals.setValidity({valueMissing:!!this.required&&!this.checked,...e},o??this.validationMessage,r)}}toggleChecked(e=!this.checked){this.checked=e}};i([T],ge.prototype,"disabled",void 0);i([l({attribute:"disabled",mode:"boolean"})],ge.prototype,"disabledAttribute",void 0);i([l({attribute:"form"})],ge.prototype,"formAttribute",void 0);i([l({attribute:"checked",mode:"boolean"})],ge.prototype,"initialChecked",void 0);i([l({attribute:"value",mode:"fromView"})],ge.prototype,"initialValue",void 0);i([l],ge.prototype,"name",void 0);i([l({mode:"boolean"})],ge.prototype,"required",void 0);var Dt=class extends ge{indeterminateChanged(e,o){this.setAriaChecked(),D(this.elementInternals,"indeterminate",o)}constructor(){super(),this.elementInternals.role="checkbox"}setAriaChecked(e=this.checked){if(this.indeterminate){this.elementInternals.ariaChecked="mixed";return}super.setAriaChecked(e)}toggleChecked(e=!this.checked){this.indeterminate=!1,super.toggleChecked(e)}};i([T],Dt.prototype,"indeterminate",void 0);i([l],Dt.prototype,"shape",void 0);i([l],Dt.prototype,"size",void 0);var St={modal:"modal",nonModal:"non-modal",alert:"alert"};function vl(t,e="-dialog"){return t?.nodeType!==Node.ELEMENT_NODE?!1:t.tagName.toLowerCase().endsWith(e)}var kl=`${g.prefix}-dialog`;var Ne=class extends y{constructor(){super(...arguments),this.emitToggle=()=>{this.$emit("toggle",{oldState:this.dialog.open?"closed":"open",newState:this.dialog.open?"open":"closed"})}}get dialogDescribedby(){if(this.dialog)return this.ariaDescribedby}get dialogLabel(){if(this.dialog)return this.ariaLabel}get dialogLabelledby(){if(this.dialog)return this.ariaLabelledby}get dialogModal(){if(this.dialog&&this.type!==St.nonModal)return!0}get dialogRole(){if(this.dialog&&this.type===St.alert)return"alertdialog"}connectedCallback(){super.connectedCallback(),O.enqueue(()=>{this.type=this.type??St.modal})}emitBeforeToggle(){this.$emit("beforetoggle",{oldState:this.dialog.open?"open":"closed",newState:this.dialog.open?"closed":"open"})}show(){O.enqueue(()=>{this.emitBeforeToggle(),this.type===St.alert||this.type===St.modal?this.dialog.showModal():this.type===St.nonModal&&this.dialog.show(),this.querySelector("[autofocus]")?.focus?.(),this.emitToggle()})}hide(){this.emitBeforeToggle(),this.dialog.close(),this.emitToggle()}clickHandler(e){return this.dialog.open&&this.type!==St.alert&&e.target===this.dialog&&this.hide(),!0}};i([T],Ne.prototype,"dialog",void 0);i([l({attribute:"aria-describedby"})],Ne.prototype,"ariaDescribedby",void 0);i([l({attribute:"aria-labelledby"})],Ne.prototype,"ariaLabelledby",void 0);i([l({attribute:"aria-label"})],Ne.prototype,"ariaLabel",void 0);i([l],Ne.prototype,"type",void 0);i([wt],Ne.prototype,"dialogDescribedby",null);i([wt],Ne.prototype,"dialogLabel",null);i([wt],Ne.prototype,"dialogLabelledby",null);i([wt],Ne.prototype,"dialogModal",null);i([wt],Ne.prototype,"dialogRole",null);var yl=f`
  @layer base {
    :host {
      --dialog-backdrop: ${Ii};
      --dialog-starting-scale: 0.85;
    }

    ::backdrop {
      background: var(--dialog-backdrop, rgba(0, 0, 0, 0.4));
    }

    dialog {
      background: ${E};
      border-radius: ${Qi};
      border: none;
      box-shadow: ${ps};
      color: ${P};
      max-height: 100vh;
      padding: 0;
      width: 100%;
      max-width: 600px;
    }

    :host([type='non-modal']) dialog {
      inset: 0;
      z-index: 2;
      overflow: auto;
    }

    @supports (max-height: 1dvh) {
      dialog {
        max-height: 100dvh;
      }
    }
  }

  @layer animations {
    /* Disable animations for reduced motion */
    @media (prefers-reduced-motion: no-preference) {
      dialog,
      ::backdrop {
        transition: display allow-discrete, opacity, overlay allow-discrete, scale;
        transition-duration: ${vs};
        transition-timing-function: ${so};
        /* Set opacity to 0 when closed */
        opacity: 0;
      }
      ::backdrop {
        transition-timing-function: ${xs};
      }

      /* Set opacity to 1 when open */
      [open],
      [open]::backdrop {
        opacity: 1;
      }

      /* Exit styles for dialog */
      dialog:not([open]) {
        /* Make small when leaving */
        scale: var(--dialog-starting-scale);
        /* Faster leaving the stage then entering */
        transition-timing-function: ${io};
      }
    }

    @starting-style {
      [open],
      [open]::backdrop {
        opacity: 0;
      }

      dialog {
        scale: var(--dialog-starting-scale);
      }
    }
  }

  @media (forced-colors: active) {
    @layer base {
      dialog {
        border: ${M} solid ${K};
      }
    }
  }
`;var xl=m`
  <dialog
    class="dialog"
    part="dialog"
    aria-modal="${t=>t.dialogModal}"
    aria-describedby="${t=>t.dialogDescribedby}"
    aria-labelledby="${t=>t.dialogLabelledby}"
    aria-label="${t=>t.dialogLabel}"
    role="${t=>t.dialogRole}"
    @click="${(t,e)=>t.clickHandler(e.event)}"
    @cancel="${t=>t.hide()}"
    ${de("dialog")}
  >
    <div tabindex="-1"></div>
    <slot></slot>
  </dialog>
`;var Sl={name:kl,registry:g.registry,styles:yl,template:xl};var qr=class extends y{clickHandler(e){if(!e.defaultPrevented){let o=this.parentElement;vl(o)&&o.hide()}return!0}};var Bl=`${g.prefix}-dialog-body`;var Nl=f`
  ${S("grid")}

  :host {
    background: ${E};
    box-sizing: border-box;
    gap: ${te};
    padding: ${no} ${fs};
    container: dialog-body / inline-size;
  }

  .title {
    box-sizing: border-box;
    align-items: flex-start;
    background: ${E};
    color: ${P};
    column-gap: 8px;
    display: flex;
    font-family: ${C};
    font-size: ${eo};
    font-weight: ${he};
    inset-block-start: 0;
    justify-content: space-between;
    line-height: ${jr};
    margin-block-end: calc(${te} * -1);
    margin-block-start: calc(${no} * -1);
    padding-block-end: ${te};
    padding-block-start: ${no};
  }

  .content {
    box-sizing: border-box;
    color: ${P};
    font-family: ${C};
    font-size: ${j};
    font-weight: ${_};
    line-height: ${Y};
    min-height: 32px;
  }

  .actions {
    box-sizing: border-box;
    background: ${E};
    display: flex;
    flex-direction: column;
    gap: ${te};
    inset-block-end: 0;
    margin-block-end: calc(${no} * -1);
    padding-block-end: ${no};
    padding-block-start: ${ro};
  }

  ::slotted([slot='title-action']) {
    margin-inline-start: auto;
  }

  ::slotted([slot='title']) {
    font: inherit;
    padding: 0;
    margin: 0;
  }

  /* align  title content to the end when there is no title*/
  :not(:has(:is([slot='title'], [slot='title-action']))) .title {
    justify-content: end;
  }

  @container (min-width: 480px) {
    .actions {
      align-items: center;
      flex-direction: row;
      justify-content: flex-end;
      margin-block-start: calc(${te} * -1);
      padding-block-start: ${te};
    }
  }

  /* For a11y, set sticky position for title and actions when the viewport is tall enough */
  @media (min-height: 480px) {
    .title {
      position: sticky;
      z-index: 1;
    }
    .actions {
      position: sticky;
      z-index: 2;
    }
  }
`;var $l=m`
  <template>
    <div class="title" part="title">
      <slot name="title"></slot>
      <slot name="title-action"></slot>
      <slot name="close" @click="${(t,e)=>t.clickHandler(e.event)}"></slot>
    </div>
    <div class="content" part="content"><slot></slot></div>
    <div class="actions" part="actions"><slot name="action"></slot></div>
  </template>
`;var Pl={name:Bl,registry:g.registry,styles:Nl,template:$l};var uo={horizontal:"horizontal",vertical:"vertical"};var po={separator:"separator",presentation:"presentation"},Un=uo;var wl=`${g.prefix}-divider`;var ho=class extends y{constructor(){super(...arguments),this.elementInternals=this.attachInternals()}connectedCallback(){super.connectedCallback(),this.elementInternals.role=this.role??po.separator,this.role!==po.presentation&&(this.elementInternals.ariaOrientation=this.orientation??Un.horizontal)}roleChanged(e,o){this.$fastController.isConnected&&(this.elementInternals.role=`${o??po.separator}`),o===po.presentation&&(this.elementInternals.ariaOrientation=null)}orientationChanged(e,o){this.elementInternals.ariaOrientation=this.role!==po.presentation?o??null:null,De(this.elementInternals,e,o,Un)}};i([l],ho.prototype,"role",void 0);i([l],ho.prototype,"orientation",void 0);var Lt=class extends ho{};i([l({attribute:"align-content"})],Lt.prototype,"alignContent",void 0);i([l],Lt.prototype,"appearance",void 0);i([l({mode:"boolean"})],Lt.prototype,"inset",void 0);var Fl=f`
  ${S("flex")}

  :host {
    --_vertical-min-height: 10px;

    contain: content;
  }

  :host::after,
  :host::before {
    align-self: center;
    background: ${Jt};
    box-sizing: border-box;
    content: '';
    display: flex;
    flex-grow: 1;
    height: ${M};
  }

  :host([inset]) {
    padding: 0 12px;
  }

  :host ::slotted(*) {
    color: ${Re};
    font-family: ${C};
    font-size: ${R};
    font-weight: ${_};
    margin: 0;
    padding: 0 12px;
  }

  :host([align-content='start'])::before,
  :host([align-content='end'])::after {
    flex-basis: 12px;
    flex-grow: 0;
    flex-shrink: 0;
  }

  :host([orientation='vertical']) {
    align-items: center;
    flex-direction: column;
    height: 100%;
  }

  :host([orientation='vertical'][inset])::before {
    margin-top: 12px;
  }

  :host([orientation='vertical'][inset])::after {
    margin-bottom: 12px;
  }

  :host([orientation='vertical'])::before,
  :host([orientation='vertical'])::after {
    width: ${M};
    min-height: var(--_vertical-min-height);
    height: 100%;
  }

  /**
   * Use @supports to avoid the scoped styles from applying in Safari. Because
   * Safari supports @scope but ':scope:has(*)' would select a host with no
   * children.
   */
  @supports not selector(:host(:has(*))) {
    @scope {
      :scope:has(*) {
        --_vertical-min-height: 20px;
      }
    }
  }

  :host(:has(*)) {
    --_vertical-min-height: 20px;
  }

  :host([orientation='vertical']) ::slotted(*) {
    display: flex;
    flex-direction: column;
    padding: 12px 0;
    line-height: 20px;
  }

  :host([orientation='vertical'][align-content='start'])::before {
    min-height: 8px;
  }
  :host([orientation='vertical'][align-content='end'])::after {
    min-height: 8px;
  }

  :host([appearance='strong'])::before,
  :host([appearance='strong'])::after {
    background: ${Ve};
  }
  :host([appearance='strong']) ::slotted(*) {
    color: ${P};
  }
  :host([appearance='brand'])::before,
  :host([appearance='brand'])::after {
    background: ${Tr};
  }
  :host([appearance='brand']) ::slotted(*) {
    color: ${Do};
  }
  :host([appearance='subtle'])::before,
  :host([appearance='subtle'])::after {
    background: ${Li};
  }
  :host([appearance='subtle']) ::slotted(*) {
    color: ${W};
  }

  @media (forced-colors: active) {
    :host([appearance='strong'])::before,
    :host([appearance='strong'])::after,
    :host([appearance='brand'])::before,
    :host([appearance='brand'])::after,
    :host([appearance='subtle'])::before,
    :host([appearance='subtle'])::after,
    :host::after,
    :host::before {
      background: WindowText;
      color: WindowText;
    }
  }
`;function zu(){return m`<slot></slot>`}var Tl=zu();var Al={name:wl,registry:g.registry,styles:Fl,template:Tl};var Du=0;function ut(t="id-"){let e=`${t}${Du++}`;return document.getElementById(e)?ut(t):e}var Cl={above:"above",after:"after",before:"before"},$e={badInput:"bad-input",customError:"custom-error",patternMismatch:"pattern-mismatch",rangeOverflow:"range-overflow",rangeUnderflow:"range-underflow",stepMismatch:"step-mismatch",tooLong:"too-long",tooShort:"too-short",typeMismatch:"type-mismatch",valueMissing:"value-missing",valid:"valid"},Il=`${g.prefix}-field`;var Bt=class extends y{get slottedInputObserver(){return this._slottedInputObserver??=new MutationObserver(()=>{this.setStates()}),this._slottedInputObserver}labelSlotChanged(e,o){o&&this.input&&(this.setLabelProperties(),this.setStates())}messageSlotChanged(e,o){D(this.elementInternals,"has-message",!!o.length)}slottedInputsChanged(e,o){let r=o?.filter(n=>n.nodeType===Node.ELEMENT_NODE)??[];r?.length&&(this.input=r?.[0])}inputChanged(e,o){o&&(this.setStates(),this.setLabelProperties(),this.slottedInputObserver.observe(o,{attributes:!0,attributeFilter:["disabled","required","readonly"],subtree:!0}))}changeHandler(e){return this.setStates(),this.setValidationStates(),!0}clickHandler(e){return this===e.target&&this.input.click(),!0}constructor(){super(),this.labelSlot=[],this.elementInternals=this.attachInternals(),this.elementInternals.role="presentation"}connectedCallback(){super.connectedCallback(),this.addEventListener("invalid",this.invalidHandler,{capture:!0})}disconnectedCallback(){this.slottedInputObserver.disconnect(),this.removeEventListener("invalid",this.invalidHandler,{capture:!0}),super.disconnectedCallback()}focusinHandler(e){return this.matches(":focus-within:has(> :focus-visible)")&&D(this.elementInternals,"focus-visible",!0),!0}focusoutHandler(e){return D(this.elementInternals,"focus-visible",!1),!0}invalidHandler(e){this.messageSlot.length&&e.preventDefault(),this.setValidationStates()}async setLabelProperties(){let e=await this.getLabelableInput();e&&(e.id||=ut("input"),this.labelSlot?.forEach(o=>{o instanceof HTMLLabelElement&&(o.htmlFor||=e.id,o.id||=`${e.id}--label`,e.setAttribute("aria-labelledby",o.id))}))}getLabelableInput(){return new Promise(e=>{if(!this.input)return e(null);if(this.input.constructor.elementIdentity!=="dropdown")return e(this.input);if(this.input.control)return e(this.input.control);new MutationObserver((o,r)=>{this.input.control&&(e(this.input.control),r.disconnect())}).observe(this.input,{childList:!0})})}setStates(){this.elementInternals&&this.input&&(D(this.elementInternals,"disabled",!!this.input.disabled),D(this.elementInternals,"readonly",!!this.input.readOnly),D(this.elementInternals,"required",!!this.input.required),D(this.elementInternals,"checked",!!this.input.checked))}setValidationStates(){if(this.input?.validity)for(let[e,o]of Object.entries($e))D(this.elementInternals,o,this.input.validity[e])}};i([T],Bt.prototype,"labelSlot",void 0);i([T],Bt.prototype,"messageSlot",void 0);i([T],Bt.prototype,"slottedInputs",void 0);i([T],Bt.prototype,"input",void 0);var _o=class extends Bt{constructor(){super(...arguments),this.labelPosition=Cl.above}};i([l({attribute:"label-position"})],_o.prototype,"labelPosition",void 0);var Hl=f`
  ${S("inline-grid")}

  :host {
    color: ${P};
    align-items: center;
    gap: 0 ${ie};
    justify-items: start;
  }

  :has([slot='message']) {
    color: ${P};
    row-gap: ${te};
  }

  :not(::slotted([slot='label'])) {
    gap: 0;
  }

  :host([label-position='before']) {
    grid-template-areas: 'label input' 'label message';
  }

  :host([label-position='after']) {
    gap: 0;
    grid-template-areas: 'input label' 'message message';
    grid-template-columns: auto 1fr;
  }

  :host([label-position='after']) ::slotted([slot='input']) {
    margin-inline-end: ${ie};
  }

  :host([label-position='above']) {
    grid-template-areas: 'label' 'input' 'message';
    row-gap: ${Oo};
  }

  :host([label-position='below']) {
    grid-template-areas: 'input' 'label' 'message';
    justify-items: center;
  }

  :host([label-position='below']) ::slotted([slot='label']) {
    margin-block-start: ${ms};
  }

  :host(${sl}) ::slotted([slot='label'])::after {
    content: '*' / '';
    color: ${Qt};
    margin-inline-start: ${ae};
  }

  ::slotted([slot='input']) {
    grid-area: input;
  }

  ::slotted([slot='message']) {
    color: ${W};
    font-family: ${C};
    font-size: ${R};
    font-weight: ${_};
    grid-area: message;
    line-height: ${G};
    margin-block-start: ${Oo};
  }

  :host(${rl}:focus-within) {
    border-radius: ${A};
    outline: ${Se} solid ${ue};
  }

  ::slotted(label),
  ::slotted([slot='label']) {
    cursor: inherit;
    display: inline-flex;
    font-family: ${C};
    font-size: ${j};
    font-weight: ${_};
    grid-area: label;
    line-height: ${Y};
    justify-self: stretch;
    user-select: none;
  }

  :host([size='small']) ::slotted(label) {
    font-size: ${R};
    line-height: ${G};
  }

  :host([size='large']) ::slotted(label) {
    font-size: ${pe};
    line-height: ${xe};
  }

  :host([size='large']) ::slotted(label),
  :host([weight='semibold']) ::slotted(label) {
    font-weight: ${he};
  }

  :host(${je}) {
    cursor: default;
  }

  ::slotted([flag]) {
    display: none;
  }

  :host(${tl}) ::slotted([flag='${$e.badInput}']),
  :host(${ol}) ::slotted([flag='${$e.customError}']),
  :host(${nl}) ::slotted([flag='${$e.patternMismatch}']),
  :host(${al}) ::slotted([flag='${$e.rangeOverflow}']),
  :host(${il}) ::slotted([flag='${$e.rangeUnderflow}']),
  :host(${ll}) ::slotted([flag='${$e.stepMismatch}']),
  :host(${cl}) ::slotted([flag='${$e.tooLong}']),
  :host(${dl}) ::slotted([flag='${$e.tooShort}']),
  :host(${ul}) ::slotted([flag='${$e.typeMismatch}']),
  :host(${gl}) ::slotted([flag='${$e.valueMissing}']),
  :host(${hl}) ::slotted([flag='${$e.valid}']) {
    display: block;
  }
`;var zl=m`
  <template
    @click="${(t,e)=>t.clickHandler(e.event)}"
    @change="${(t,e)=>t.changeHandler(e.event)}"
    @focusin="${(t,e)=>t.focusinHandler(e.event)}"
    @focusout="${(t,e)=>t.focusoutHandler(e.event)}"
  >
    <slot name="label" part="label" ${oe("labelSlot")}></slot>
    <slot name="input" part="input" ${oe("slottedInputs")}></slot>
    <slot name="message" part="message" ${oe({property:"messageSlot",filter:wn("[flag]")})}></slot>
  </template>
`;var Dl={name:Il,registry:g.registry,shadowOptions:{delegatesFocus:!0},styles:Hl,template:zl};var Nt=class extends y{constructor(){super(...arguments),this.disabled=!1,this.required=!1}};i([l],Nt.prototype,"size",void 0);i([l],Nt.prototype,"weight",void 0);i([l({mode:"boolean"})],Nt.prototype,"disabled",void 0);i([l({mode:"boolean"})],Nt.prototype,"required",void 0);var Ll=`${g.prefix}-label`;var El=f`
  ${S("inline-flex")}

  :host {
    color: ${P};
    cursor: pointer;
    font-family: ${C};
    font-size: ${j};
    font-weight: ${_};
    line-height: ${Y};
    user-select: none;
  }

  .asterisk {
    color: ${Qt};
    margin-inline-start: ${ae};
  }

  :host([size='small']) {
    font-size: ${R};
    line-height: ${G};
  }

  :host([size='large']) {
    font-size: ${pe};
    line-height: ${xe};
  }

  :host(:is([size='large'], [weight='semibold'])) {
    font-weight: ${he};
  }

  :host([disabled]),
  :host([disabled]) .asterisk {
    color: ${z};
  }
`;function Lu(){return m`
    <slot></slot>
    <span part="asterisk" class="asterisk" aria-hidden="true" ?hidden="${t=>!t.required}">*</span>
  `}var Ol=Lu();var Rl={name:Ll,registry:g.registry,styles:El,template:Ol};var Ml=f`
  ${Wn}

  ::slotted(a) {
    position: absolute;
    inset: 0;
  }

  @media (forced-colors: active) {
    :host {
      border-color: LinkText;
      color: LinkText;
    }
  }
`;function Eu(t={}){return m`
    <template
      tabindex="0"
      @click="${(e,o)=>e.clickHandler(o.event)}"
      @keydown="${(e,o)=>e.keydownHandler(o.event)}"
    >
      ${Ye(t)}
      <span class="content" part="content">
        <slot></slot>
      </span>
      ${Ke(t)}
    </template>
  `}var _l=Eu();var Vl={name:Is,registry:g.registry,styles:Ml,template:_l};var pt=class extends Be{constructor(){super(...arguments),this.iconOnly=!1}appearanceChanged(e,o){De(this.elementInternals,e,o,Fs)}shapeChanged(e,o){De(this.elementInternals,e,o,Ts)}sizeChanged(e,o){De(this.elementInternals,e,o,As)}iconOnlyChanged(e,o){D(this.elementInternals,"icon",!!o)}};i([l],pt.prototype,"appearance",void 0);i([l],pt.prototype,"shape",void 0);i([l],pt.prototype,"size",void 0);i([l({attribute:"icon-only",mode:"boolean"})],pt.prototype,"iconOnly",void 0);Je(pt,Ce);var Wl=`${g.prefix}-message-bar`;var jl=f`
  :host {
    display: grid;
    box-sizing: border-box;
    font-family: ${C};
    font-size: ${R};
    line-height: ${G};
    width: 100%;
    background: ${Ct};
    color: ${W};
    border: 1px solid ${Ve};
    padding-inline: ${ie};
    border-radius: ${A};
    min-height: 36px;
    align-items: center;
    grid-template: 'icon body actions dismiss' / auto 1fr auto auto;
    contain: layout style paint;
  }

  :host([shape='square']) {
    border-radius: 0;
  }

  :host([intent='success']) {
    background-color: ${Dr};
    border-color: ${Vi};
  }

  :host([intent='warning']) {
    background-color: ${Er};
    border-color: ${Rr};
  }

  :host([intent='error']) {
    background-color: ${Ir};
    border-color: ${zr};
  }

  :host([layout='multiline']) {
    grid-template-areas:
      'icon body dismiss'
      'actions actions actions';
    grid-template-columns: auto 1fr auto;
    grid-template-rows: auto auto 1fr;
    padding-block: ${oo};
    padding-inline: ${ie};
  }

  .content {
    grid-area: body;
    padding-block: ${oo};
    padding-inline: 0;
  }

  :host([layout='multiline']) .content {
    padding: 0;
  }

  ::slotted([slot='icon']) {
    display: flex;
    grid-area: icon;
    flex-direction: column;
    align-items: center;
    color: ${W};
    margin-inline-end: ${Qe};
  }

  :host([layout='multiline']) ::slotted([slot='icon']) {
    align-items: start;
    height: 100%;
  }

  ::slotted([slot='dismiss']) {
    grid-area: dismiss;
  }

  .actions {
    grid-area: actions;
    display: flex;
    justify-self: end;
    margin-inline-end: ${Qe};
    gap: ${Qe};
  }

  :host([layout='multiline']) .actions {
    margin-block-start: ${oo};
    margin-inline-end: 0;
  }

  :host([layout='multiline']) ::slotted([slot='dismiss']) {
    align-items: start;
    height: 100%;
  }

  ::slotted(*) {
    font-size: inherit;
  }
`;function Ou(){return m`
    <slot name="icon"></slot>
    <div class="content">
      <slot></slot>
    </div>
    <div class="actions">
      <slot name="actions"></slot>
    </div>
    <slot name="dismiss"></slot>
  `}var Gl=Ou();var Xl={name:Wl,registry:g.registry,styles:jl,template:Gl};var Et=class extends y{constructor(){super(),this.elementInternals=this.attachInternals(),this.dismissMessageBar=()=>{this.$emit("dismiss",{})},this.elementInternals.role="status"}};i([l],Et.prototype,"shape",void 0);i([l],Et.prototype,"layout",void 0);i([l],Et.prototype,"intent",void 0);var go={INFERRED_ROLE:"data-fg-ir",ITEM:"data-fg-item",AUTHOR_TABINDEX:"data-fg-ati",SEGMENT:"data-fg-seg",SEGMENT_START:"data-fg-segs"},ht={TOOLBAR:"toolbar",TABLIST:"tablist",RADIOGROUP:"radiogroup",LISTBOX:"listbox",MENU:"menu",MENUBAR:"menubar",NONE:"none"},jv=[ht.TOOLBAR,ht.TABLIST,ht.RADIOGROUP,ht.LISTBOX,ht.MENU,ht.MENUBAR,ht.NONE];function Ur(t,e){return t.contains(e)}function ql(){return"focusgroup"in(globalThis?.HTMLElement?.prototype??{})||"focusGroup"in(globalThis?.HTMLElement?.prototype??{})}function Ul(t,e,o){let r="forward",n="backward",a="block",s="inline";if(Ru(t.composedPath()[0]))return t.key==="Tab"?t.shiftKey?n:r:null;if(t.shiftKey||t.ctrlKey||t.metaKey)return null;let{writingMode:d,direction:p}=window.getComputedStyle(e),h=!d.startsWith("horizontal-"),u=p==="rtl",v=h?a:s,B=h?s:a,F=h?d.endsWith("-rl")!==u:u,H=h&&u,fe={ArrowUp:{axis:B,dir:H?r:n},ArrowDown:{axis:B,dir:H?n:r},ArrowLeft:{axis:v,dir:F?r:n},ArrowRight:{axis:v,dir:F?n:r},Home:{dir:"start"},End:{dir:"end"}}[t.key];return!fe||o&&fe.axis&&fe.axis!==o?null:fe.dir}function Ru(t){return t?.nodeType===Node.ELEMENT_NODE&&(["INPUT","TEXTAREA","SELECT"].includes(t.nodeName)&&!["checkbox","radio"].includes(t.getAttribute("type"))||t.isContentEditable||["AUDIO","VIDEO"].includes(t.nodeName)&&t.hasAttribute("controls")||["IFRAME","OBJECT"].includes(t.nodeName))}globalThis.__FOCUSGROUP_POLYFILL__??={o:new Set,b:!1};var Kl=globalThis.__FOCUSGROUP_POLYFILL__;var Mu=Kl.o;function Kr(){for(let t of Mu)t.takeRecords()}var Vo=class{#t;#e;#n=null;#p=void 0;#c=!1;#s=!0;#r;#o=null;#a=!1;#l=null;#h=new AbortController;#d;#u;constructor(t,e,o={}){if(ql()||!t)return;this.#t=t,this.#e=e,this.#d=o.decorateOwner,this.#u=o.decorateItem,this.#g(o.definition),this.#d?.(this.#t,this.#n),this.#f();let r={signal:this.#h.signal};this.#t.addEventListener("keydown",this.#k.bind(this),r),this.#t.addEventListener("focusin",this.#y.bind(this),r),this.#t.addEventListener("focusout",this.#x.bind(this),r)}disconnect(){this.#i(),this.#h.abort(),this.#e?.disconnect?.(),this.#t=null}update(t={}){if(this.#t){if(t.definition!==void 0&&(this.#g(t.definition),this.#d?.(this.#t,this.#n)),t.authorTabindexChanges)for(let e of t.authorTabindexChanges)e.setAttribute(go.AUTHOR_TABINDEX,e.getAttribute("tabindex")??"none");this.#m(),this.#f()}}#g(t){this.#n=t?.behavior??null,this.#c=t?.wrap??!1,this.#p=t?.axis,this.#s=t?.memory??!0,this.#s||(this.#o=null)}#f(){if(!this.#n||this.#n===ht.NONE){this.#m();return}this.#e.decorate?.();for(let{element:e,segmentBoundary:o}of this.#e.items())this.#u?.(e,this.#n),e.setAttribute(go.AUTHOR_TABINDEX,e.getAttribute("tabindex")??"none"),e.tabIndex=o?0:-1;(!this.#o?.isConnected||!(this.#e.isItem?.(this.#o)??this.#e.contains(this.#o)))&&(this.#o=null);let t=this.#o??this.#e.start??this.#e.first?.()??null;t&&(t.tabIndex=0,this.#r=t,this.#i(),this.#b(t)),this.#e.flush?.()}#m(){this.#i();let t=!1;for(let{element:e}of this.#e.items()){t=!0,this.#u?.(e,null);let o=e.getAttribute(go.AUTHOR_TABINDEX);o&&(o==="none"?e.removeAttribute("tabindex"):e.setAttribute("tabindex",o),e.removeAttribute(go.AUTHOR_TABINDEX))}this.#e.undecorate?.(),t&&this.#e.flush?.()}#k(t){let e=t.composedPath()[0];if(t.defaultPrevented||e===this.#t||!this.#e.contains(e))return;let o;switch(Ul(t,e,this.#p)){case"start":o=this.#e.first();break;case"end":o=this.#e.last();break;case"forward":o=this.#e.next(e),!o&&this.#c&&(o=this.#e.first());break;case"backward":o=this.#e.previous(e),!o&&this.#c&&(o=this.#e.last());break}o&&o!==e&&(this.#v(e,o,!0),this.#o=o,t.preventDefault())}#y(t){let e=t.composedPath()[0];if(e===this.#t&&this.#a&&(!t.relatedTarget||!Ur(this.#t,t.relatedTarget))){let r=this.#o||this.#r;this.#i(),r&&r.focus(),t.stopPropagation();return}if(!this.#e.contains(e))return;this.#a&&this.#i();let o=this.#o;if(this.#o=e,o!==e&&e.tabIndex<0){let r=o??this.#r;r&&this.#v(r,e)}}#x(t){if(!t.relatedTarget||!Ur(this.#t,t.relatedTarget)){let r=this.#s?this.#o||this.#r:this.#r;r&&this.#b(r)}if(t.relatedTarget&&Ur(this.#t,t.relatedTarget)||this.#s||!this.#r)return;let e=this.#o;this.#o=null;let o=this.#e.start??this.#e.first?.()??null;if(e!==this.#r||o!==this.#r){for(let{element:r,segmentBoundary:n}of this.#e.items())r.tabIndex=n?0:-1;o&&(o.tabIndex=0,this.#r=o),this.#e.flush?.()}}#b(t){let e=(t.assignedSlot??t).getRootNode(),o=e instanceof ShadowRoot&&e.host.hasAttribute(go.AUTHOR_TABINDEX);this.#a||!o||(this.#l=this.#t.getAttribute("tabindex"),this.#t.tabIndex=0,this.#a=!0,Kr())}#i(){this.#a&&(this.#l!==null?this.#t.setAttribute("tabindex",this.#l):this.#t.removeAttribute("tabindex"),this.#a=!1,this.#l=null,this.#e.flush?.(),Kr())}#v(t,e,o=!1){e.tabIndex=0,o&&e.focus(),t.tabIndex=this.#e.sameSegment?.(t,e)??!0?-1:0,this.#i(),Kr()}};var fo=class{constructor(e,o){this.getItems=e,this.getStart=o}get start(){return this.getStart?.()??null}first(){return this.getItems()[0]??null}last(){let e=this.getItems();return e[e.length-1]??null}next(e){let o=this.getItems(),r=o.indexOf(e);return r===-1?null:o[r+1]??null}previous(e){let o=this.getItems(),r=o.indexOf(e);return r<=0?null:o[r-1]??null}*items(){for(let e of this.getItems())yield{element:e}}contains(e){return this.getItems().includes(e)}};var Yl={success:"success",warning:"warning",error:"error"},Jl=`${g.prefix}-progress-bar`;var Ql=f`
  ${S("block")}

  :host {
    width: 100%;
    height: 2px;
    overflow-x: hidden;
    background-color: ${$r};
    border-radius: ${A};
    contain: content;

    @supports (width: attr(value type(<number>))) {
      --max: attr(max type(<number>), 100);
      --min: attr(min type(<number>), 0);
      --value: attr(value type(<number>), 0);
      --indicator-width: clamp(0%, calc((var(--value) - var(--min)) / (var(--max) - var(--min)) * 100%), 100%);
    }
  }

  :host([thickness='large']) {
    height: 4px;
  }

  :host([shape='square']) {
    border-radius: ${Zt};
  }

  .indicator {
    background-color: ${it};
    border-radius: inherit;
    height: 100%;
  }

  :host([value]) .indicator {
    transition: all 0.2s ease-in-out;

    @supports (width: attr(value type(<number>))) {
      width: var(--indicator-width);
    }
  }

  :host(:not([value])) .indicator {
    position: relative;
    width: 33%;
    background-image: linear-gradient(
      to right,
      ${$r} 0%,
      ${re} 50%,
      ${$r} 100%
    );
    animation-name: indeterminate;
    animation-duration: 3s;
    animation-timing-function: linear;
    animation-iteration-count: infinite;
  }

  :host([validation-state='error']) .indicator {
    background-color: ${Hr};
  }

  :host([validation-state='warning']) .indicator {
    background-color: ${Or};
  }

  :host([validation-state='success']) .indicator {
    background-color: ${Lr};
  }

  @layer animations {
    /* Disable animations for reduced motion */
    @media (prefers-reduced-motion: no-preference) {
      :host([value]) {
        transition: none;
      }
      :host(:not([value])) .indicator {
        animation-duration: 0.01ms;
        animation-iteration-count: 1;
      }
    }
  }

  @keyframes indeterminate {
    0% {
      inset-inline-start: -33%;
    }
    100% {
      inset-inline-start: 100%;
    }
  }

  @media (forced-colors: active) {
    :host {
      background-color: CanvasText;
    }
    .indicator,
    :host(:is([validation-state='success'], [validation-state='warning'], [validation-state='error'])) .indicator {
      background-color: Highlight;
    }
  }
`;function _u(){return m` <div class="indicator" part="indicator" ${de("indicator")}></div> `}var Zl=_u();var ec={name:Jl,registry:g.registry,styles:Ql,template:Zl};var gt=class extends y{indicatorChanged(){this.setIndicatorWidth()}validationStateChanged(e,o){De(this.elementInternals,e,o,Yl)}valueChanged(e,o){this.elementInternals&&(this.elementInternals.ariaValueNow=typeof o=="number"?`${o}`:null),this.setIndicatorWidth()}minChanged(e,o){this.elementInternals&&(this.elementInternals.ariaValueMin=typeof o=="number"?`${o}`:null),this.setIndicatorWidth()}maxChanged(e,o){this.elementInternals&&(this.elementInternals.ariaValueMax=typeof o=="number"?`${o}`:null),this.setIndicatorWidth()}constructor(){super(),this.elementInternals=this.attachInternals(),this.validationState=null,this.elementInternals.role="progressbar"}setIndicatorWidth(){CSS.supports("width: attr(value type(<number>))")||O.enqueue(()=>{if(typeof this.value!="number"){this.indicator?.style.removeProperty("width");return}let e=this.min??0,o=this.max??100,r=this.value??0,n=o-e,a=n===0?0:Math.fround((r-e)/n*100);this.indicator?.style.setProperty("width",`${a}%`)})}};i([T],gt.prototype,"indicator",void 0);i([l({attribute:"validation-state"})],gt.prototype,"validationState",void 0);i([l({converter:ve})],gt.prototype,"value",void 0);i([l({converter:ve})],gt.prototype,"min",void 0);i([l({converter:ve})],gt.prototype,"max",void 0);var mo=class extends gt{};i([l],mo.prototype,"thickness",void 0);i([l],mo.prototype,"shape",void 0);function tc(t,e="-radio"){return ci(e)(t)}var oc=`${g.prefix}-radio`;var Kn=uo,rc=`${g.prefix}-radio-group`;var Le=class extends y{checkedIndexChanged(e,o){this.enabledRadios&&this.checkRadio(o)}disabledChanged(e,o){this.radios&&(this.checkedIndex=-1,this.radios?.forEach(r=>{r.disabled=!!r.disabledAttribute||!!this.disabled}))}initialValueChanged(e,o){this.value=o??""}nameChanged(e,o){this.isConnected&&o&&this.radios?.forEach(r=>{r.name=this.name})}orientationChanged(e,o){this.elementInternals.ariaOrientation=this.orientation??Kn.horizontal}radiosChanged(e,o){let r=o?.length;if(!r)return;!this.name&&o.every(s=>s.name===o[0].name)&&(this.name=o[0].name);let n=this.enabledRadios.findLastIndex(s=>s.initialChecked);o.forEach((s,d)=>{s.ariaPosInSet=`${d+1}`,s.ariaSetSize=`${r}`,this.initialValue&&!this.dirtyState?s.checked=s.value===this.initialValue:s.checked=d===n,s.name=this.name??s.name,s.disabled=!!this.disabled||!!s.disabledAttribute,s.toggleAttribute("focusgroupstart",s.checked&&!s.disabled)}),!this.dirtyState&&this.initialValue&&(this.value=this.initialValue),(!this.value||this.value&&typeof this.checkedIndex!="number"&&n>=0)&&(this.checkedIndex=n);let a=o.map(s=>s.id).join(" ").trim();a&&this.setAttribute("aria-owns",a)}requiredChanged(e,o){this.elementInternals.ariaRequired=o?"true":null,this.setValidity()}slottedRadiosChanged(e,o){O.enqueue(()=>{this.radios=[...this.querySelectorAll("*")].filter(r=>tc(r))})}get enabledRadios(){return this.disabled?[]:this.radios?.filter(e=>!e.disabled)??[]}static{this.formAssociated=!0}get validationMessage(){if(this.elementInternals.validationMessage)return this.elementInternals.validationMessage;if(this.enabledRadios?.[0]?.validationMessage)return this.enabledRadios[0].validationMessage;if(!this._validationFallbackMessage){let e=document.createElement("input");e.type="radio",e.required=!0,e.checked=!1,this._validationFallbackMessage=e.validationMessage}return this._validationFallbackMessage}get validity(){return this.elementInternals.validity}get value(){return k.notify(this,"value"),this.enabledRadios.find(e=>e.checked)?.value??null}set value(e){let o=this.enabledRadios.findIndex(r=>r.value===e);this.checkedIndex=o,this.$fastController.isConnected&&(this.setFormValue(e),this.setValidity()),k.track(this,"value")}changeHandler(e){if(this===e.target)return!0;this.dirtyState=!0;let o=this.enabledRadios.indexOf(e.target);return this.checkRadio(o),this.radios?.filter(r=>r.disabled)?.forEach(r=>{r.checked=!1}),!0}checkRadio(e=this.checkedIndex,o=!1){let r=this.checkedIndex;this.enabledRadios.forEach((n,a)=>{let s=a===e;n.checked=s,s&&(r=a,o&&n.$emit("change"))}),this.checkedIndex=r,this.setFormValue(this.value),this.setValidity()}checkValidity(){return this.elementInternals.checkValidity()}clickHandler(e){return this===e.target&&this.enabledRadios[Math.max(0,this.checkedIndex)]?.focus(),!0}constructor(){super(),this.isNavigating=!1,this.dirtyState=!1,this.elementInternals=this.attachInternals(),this.elementInternals.role="radiogroup",this.elementInternals.ariaOrientation=this.orientation??Kn.horizontal}focus(){this.enabledRadios[Math.max(0,this.checkedIndex)]?.focus()}formResetCallback(){this.dirtyState=!1,this.checkedIndex=-1,this.setFormValue(this.value),this.setValidity()}focusinHandler(e){if(!this.disabled&&(this.isNavigating||this.value)){this.radios?.forEach(r=>{r.disabled&&r.checked&&(r.checked=!1)});let o=this.enabledRadios.indexOf(e.target);o>-1&&this.checkRadio(o,!0),this.isNavigating=!1}return!0}keydownHandler(e){switch(e.key){case"ArrowUp":case"ArrowDown":case"ArrowLeft":case"ArrowRight":case"Home":case"End":this.isNavigating=!0;break;case" ":this.checkRadio();break}return!0}disabledRadioHandler(e){e.detail===!0&&e.target.checked&&(this.checkedIndex=-1)}reportValidity(){return this.elementInternals.reportValidity()}setFormValue(e,o){this.elementInternals.setFormValue(e,e??o)}setValidity(e,o,r){if(this.$fastController.isConnected){if(!(this.required&&!this.value&&!this.disabled)){this.enabledRadios?.forEach(d=>{d.elementInternals.setValidity({})});return}let a={valueMissing:!0,...e},s=o??this.validationMessage;this.enabledRadios?.forEach((d,p)=>{p===0?d.elementInternals.setValidity(a,s,d):d.elementInternals.setValidity({})})}}};i([T],Le.prototype,"checkedIndex",void 0);i([l({attribute:"disabled",mode:"boolean"})],Le.prototype,"disabled",void 0);i([l({attribute:"value",mode:"fromView"})],Le.prototype,"initialValue",void 0);i([l],Le.prototype,"name",void 0);i([l],Le.prototype,"orientation",void 0);i([T],Le.prototype,"radios",void 0);i([l({mode:"boolean"})],Le.prototype,"required",void 0);i([T],Le.prototype,"slottedRadios",void 0);var Yr=class extends Le{disconnectedCallback(){this.fg?.disconnect(),super.disconnectedCallback()}radiosChanged(e,o){super.radiosChanged(e,o),this.fgItems??=new fo(()=>this.enabledRadios?.filter(r=>!r.hidden)??[],()=>this.enabledRadios?.find(r=>r.checked)??null),this.fg?this.fg.update():this.fg=new Vo(this,this.fgItems,{definition:{behavior:"radiogroup",axis:void 0,wrap:!0}})}};var nc=f`
  ${S("flex")}

  :host {
    -webkit-tap-highlight-color: transparent;
    cursor: pointer;
    gap: ${ro};
  }

  :host([orientation='vertical']) {
    flex-direction: column;
    justify-content: flex-start;
  }

  :host([orientation='horizontal']) {
    flex-direction: row;
  }

  ::slotted(*) {
    color: ${W};
  }

  ::slotted(:hover) {
    color: ${Re};
  }

  ::slotted(:active) {
    color: ${P};
  }

  ::slotted(${je}) {
    color: ${z};
  }

  ::slotted(${x}) {
    color: ${P};
  }

  :host([slot='input']) {
    margin: ${te} ${Qe};
  }
`;function Vu(){return m`
    <template
      focusgroup="radiogroup wrap"
      @disabled="${(t,e)=>t.disabledRadioHandler(e.event)}"
      @change="${(t,e)=>t.changeHandler(e.event)}"
      @click="${(t,e)=>t.clickHandler(e.event)}"
      @focusin="${(t,e)=>t.focusinHandler(e.event)}"
      @keydown="${(t,e)=>t.keydownHandler(e.event)}"
    >
      <slot ${oe("slottedRadios")}></slot>
    </template>
  `}var ac=Vu();var ic={name:rc,registry:g.registry,styles:nc,template:ac};var sc=f`
  ${S("inline-flex")}

  :host {
    --size: 16px;
    aspect-ratio: 1;
    background-color: ${E};
    border: ${M} solid ${Fe};
    border-radius: ${ee};
    box-sizing: border-box;
    position: relative;
    width: var(--size);
  }

  :host([size='large']) {
    --size: 20px;
  }

  .checked-indicator {
    aspect-ratio: 1;
    border-radius: ${ee};
    color: ${Ie};
    inset: 0;
    margin: auto;
    position: absolute;
    width: calc(var(--size) * 0.625);
  }

  :host(:not([slot='input']))::after {
    content: '' / '';
    position: absolute;
    display: block;
    inset: -8px;
    box-sizing: border-box;
    outline: none;
    border: ${Se} solid ${K};
    border-radius: ${A};
  }

  :host(:not([slot='input']):focus-visible)::after {
    border-color: ${ue};
  }

  :host(:hover) {
    border-color: ${Me};
  }

  :host(${x}) {
    border-color: ${He};
  }

  :host(${x}) .checked-indicator {
    background-color: ${it};
  }

  :host(${x}:hover) .checked-indicator {
    background-color: ${It};
  }

  :host(:active) {
    border-color: ${_e};
  }

  :host(${x}:active) .checked-indicator {
    background-color: ${Ht};
  }

  :host(:focus-visible) {
    outline: none;
  }

  :host(${je}) {
    background-color: ${yt};
    border-color: ${Z};
  }

  :host(${x}${je}) .checked-indicator {
    background-color: ${Z};
  }

  @media (forced-colors: active) {
    :host {
      border-color: FieldText;
    }

    :host(:not([slot='input']:focus-visible))::after {
      border-color: Canvas;
    }

    :host(:not(${je}):hover),
    :host(:not([slot='input']):focus-visible)::after {
      border-color: Highlight;
    }

    .checked-indicator {
      color: HighlightText;
    }

    :host(${x}) .checked-indicator {
      background-color: FieldText;
    }

    :host(${x}:not(${je}):hover) .checked-indicator {
      background-color: Highlight;
    }

    :host(${je}) {
      border-color: GrayText;
      color: GrayText;
    }

    :host(${je}${x}) .checked-indicator {
      background-color: GrayText;
    }
  }
`;var Wu=m.partial(`
    <span part="checked-indicator" class="checked-indicator" role="presentation"></span>
`);function ju(t={}){return m`
    <template
      @click="${(e,o)=>e.clickHandler(o.event)}"
      @keydown="${(e,o)=>e.keydownHandler(o.event)}"
      @keyup="${(e,o)=>e.keyupHandler(o.event)}"
    >
      <slot name="checked-indicator">${Ae(t.checkedIndicator)}</slot>
    </template>
  `}var lc=ju({checkedIndicator:Wu});var cc={name:oc,registry:g.registry,styles:sc,template:lc};var Jr=class extends ge{constructor(){super(),this.elementInternals.role="radio"}disabledChanged(e,o){super.disabledChanged(e,o),this.$emit("disabled",o,{bubbles:!0})}requiredChanged(){}setFormValue(){}setValidity(){this.elementInternals.setValidity({})}toggleChecked(e=!0){super.toggleChecked(e)}};var Qr=class extends y{constructor(){super(),this.elementInternals=this.attachInternals(),this.elementInternals.role="progressbar"}};var bo=class extends Qr{};i([l],bo.prototype,"size",void 0);i([l],bo.prototype,"appearance",void 0);var dc=`${g.prefix}-spinner`;var uc=m`
  <slot name="indicator">
    <div class="background"></div>
    <div class="progress">
      <div class="spinner">
        <div class="start">
          <div class="indicator"></div>
        </div>
        <div class="end">
          <div class="indicator"></div>
        </div>
      </div>
    </div>
  </slot>
`;var pc=f`
  ${S("inline-flex")}

  :host {
    --duration: 1.5s;
    --indicatorSize: ${ze};
    --size: 32px;
    height: var(--size);
    width: var(--size);
    contain: strict;
    content-visibility: auto;
  }

  :host([size='tiny']) {
    --indicatorSize: ${Se};
    --size: 20px;
  }
  :host([size='extra-small']) {
    --indicatorSize: ${Se};
    --size: 24px;
  }
  :host([size='small']) {
    --indicatorSize: ${Se};
    --size: 28px;
  }
  :host([size='large']) {
    --indicatorSize: ${ze};
    --size: 36px;
  }
  :host([size='extra-large']) {
    --indicatorSize: ${ze};
    --size: 40px;
  }
  :host([size='huge']) {
    --indicatorSize: ${hs};
    --size: 44px;
  }

  .progress,
  .background,
  .spinner,
  .start,
  .end,
  .indicator {
    position: absolute;
    inset: 0;
  }

  .progress,
  .spinner,
  .indicator {
    animation: none var(--duration) infinite ${lo};
  }

  .progress {
    animation-timing-function: linear;
    animation-name: spin-linear;
  }

  .background {
    border: var(--indicatorSize) solid ${Ar};
    border-radius: 50%;
  }

  :host([appearance='inverted']) .background {
    border-color: rgba(255, 255, 255, 0.2);
  }

  .spinner {
    animation-name: spin-swing;
  }

  .start {
    overflow: hidden;
    right: 50%;
  }

  .end {
    overflow: hidden;
    left: 50%;
  }

  .indicator {
    color: ${Tr};
    box-sizing: border-box;
    border-radius: 50%;
    border: var(--indicatorSize) solid transparent;
    border-block-start-color: currentcolor;
    border-right-color: currentcolor;
  }

  :host([appearance='inverted']) .indicator {
    color: ${Ei};
  }

  .start .indicator {
    rotate: 135deg; /* Starts 9 o'clock */
    inset: 0 -100% 0 0;
    animation-name: spin-start;
  }

  .end .indicator {
    rotate: 135deg; /* Ends at 3 o'clock */
    inset: 0 0 0 -100%;
    animation-name: spin-end;
  }

  @keyframes spin-linear {
    100% {
      transform: rotate(360deg);
    }
  }

  @keyframes spin-swing {
    0% {
      transform: rotate(-135deg);
    }
    50% {
      transform: rotate(0deg);
    }
    100% {
      transform: rotate(225deg);
    }
  }

  @keyframes spin-start {
    0%,
    100% {
      transform: rotate(0deg);
    }
    50% {
      transform: rotate(-80deg);
    }
  }

  @keyframes spin-end {
    0%,
    100% {
      transform: rotate(0deg);
    }
    50% {
      transform: rotate(70deg);
    }
  }

  @media (forced-colors: active) {
    .background {
      display: none;
    }
    .indicator {
      border-color: Canvas;
      border-block-start-color: Highlight;
      border-right-color: Highlight;
    }
  }
`;var hc={name:dc,registry:g.registry,styles:pc,template:uc};var gc=`${g.prefix}-switch`;var fc=f`
  ${S("inline-flex")}

  :host {
    box-sizing: border-box;
    align-items: center;
    flex-direction: row;
    outline: none;
    user-select: none;
    contain: content;
    padding: 0 ${ne};
    width: 40px;
    height: 20px;
    background-color: ${re};
    border: 1px solid ${Fe};
    border-radius: ${ee};
  }

  :host(:enabled) {
    cursor: pointer;
  }

  :host(:hover) {
    background: none;
    border-color: ${Me};
  }
  :host(:active) {
    border-color: ${_e};
  }
  :host(${N}),
  :host([readonly]) {
    border: 1px solid ${Z};
    background-color: none;
    pointer: default;
  }
  :host(${x}) {
    background: ${it};
    border-color: ${it};
  }
  :host(${x}:hover) {
    background: ${It};
    border-color: ${It};
  }
  :host(${x}:active) {
    background: ${Ht};
    border-color: ${Ht};
  }
  :host(${x}${N}) {
    background: ${yt};
    border-color: ${Z};
  }
  .checked-indicator {
    height: 14px;
    width: 14px;
    border-radius: 50%;
    margin-inline-start: 0;
    background-color: ${W};
    transition-duration: ${ao};
    transition-timing-function: ${lo};
    transition-property: margin-inline-start;
  }
  :host(${x}) .checked-indicator {
    background-color: ${Ie};
    margin-inline-start: calc(100% - 14px);
  }
  :host(${x}:hover) .checked-indicator {
    background: ${Si};
  }
  :host(${x}:active) .checked-indicator {
    background: ${Bi};
  }
  :host(:hover) .checked-indicator {
    background-color: ${hi};
  }
  :host(:active) .checked-indicator {
    background-color: ${gi};
  }
  :host(${N}) .checked-indicator,
  :host([readonly]) .checked-indicator {
    background: ${z};
  }
  :host(${x}${N}) .checked-indicator {
    background: ${z};
  }

  :host(:focus-visible) {
    outline: none;
  }

  :host(:not([slot='input']):focus-visible) {
    border-color: ${K};
    outline: ${Se} solid ${K};
    outline-offset: 1px;
    box-shadow: ${Gr}, 0 0 0 2px ${ue};
  }

  @media (forced-colors: active) {
    :host {
      border-color: InactiveBorder;
    }
    :host(${x}),
    :host(${x}:active),
    :host(${x}:hover) {
      background: Highlight;
      border-color: Highlight;
    }
    .checked-indicator,
    :host(:hover) .checked-indicator,
    :host(:active) .checked-indicator {
      background-color: CanvasText;
    }
    :host(${x}) .checked-indicator,
    :host(${x}:hover) .checked-indicator,
    :host(${x}:active) .checked-indicator {
      background-color: HighlightText;
    }
    :host(${N}) .checked-indicator,
    :host(${x}${N}) .checked-indicator {
      background-color: GrayText;
    }
  }
`;function Gu(t={}){return m`
    <template
      @click="${(e,o)=>e.clickHandler(o.event)}"
      @input="${(e,o)=>e.inputHandler(o.event)}"
      @keydown="${(e,o)=>e.keydownHandler(o.event)}"
      @keyup="${(e,o)=>e.keyupHandler(o.event)}"
    >
      <slot name="switch">${Ae(t.switch)}</slot>
    </template>
  `}var mc=Gu({switch:'<span class="checked-indicator" part="checked-indicator"></span>'});var bc={name:gc,registry:g.registry,styles:fc,template:mc};var Zr=class extends ge{constructor(){super(),this.elementInternals.role="switch"}};function Yn(t,e="-tab"){return t?.nodeType!==Node.ELEMENT_NODE?!1:t.tagName.toLowerCase().endsWith(e)}var vc=`${g.prefix}-tab`;var kc=f`
  ${S("inline-flex")}

  :host {
    position: relative;
    flex-direction: row;
    align-items: center;
    cursor: pointer;
    box-sizing: border-box;
    justify-content: center;
    line-height: ${Y};
    font-family: ${C};
    font-size: ${j};
    color: ${Re};
    fill: currentcolor;
    grid-row: 1;
    padding: ${ie} ${Te};
    border-radius: ${A};
    gap: 4px;
  }

  :host .tab-content {
    display: inline-flex;
    flex-direction: column;
    padding: 0 2px;
    grid-column: 3;
  }

  :host([aria-selected='true']) {
    color: ${P};
    font-weight: ${he};
  }

  /* adds hidden textContent to prevent shifting ui on bold / unbolding of text */
  :host .tab-content::after {
    content: var(--textContent);
    visibility: hidden;
    height: 0;
    line-height: ${Y};
    font-weight: ${he};
  }

  :host([aria-selected='true'])::after {
    background-color: ${He};
    border-radius: ${ee};
    content: '';
    inset: 0;
    position: absolute;
    z-index: 2;
  }

  :host([aria-selected='false']:hover)::after {
    background-color: ${st};
    border-radius: ${ee};
    content: '';
    inset: 0;
    position: absolute;
    z-index: 1;
  }

  /*
   * TODO: Remove '(text-size-adjust: auto)' after this bug is fixed:
   * https://bugs.webkit.org/show_bug.cgi?id=298646
   * Also remove the same trick from tablist.styles.ts.
   * Using '@supports (text-size-adjust: auto)' here to exclude Safari 26 from
   * using CSS Anchor Positioning here because it crashes.
   */
  @supports (anchor-name: --a) and (text-size-adjust: auto) {
    :host([aria-selected='true'])::after {
      background-color: transparent;
    }

    :host([aria-selected='true']:hover)::after {
      background-color: ${st};
    }
  }

  :host([aria-selected='true'][disabled])::after {
    background-color: ${z};
  }

  ::slotted([slot='start']) {
    grid-column: 2;
  }

  ::slotted([slot='end']) {
    grid-column: -1;
  }

  ::slotted([slot='start']),
  ::slotted([slot='end']) {
    display: flex;
  }
  :host([disabled]) {
    cursor: not-allowed;
    fill: ${z};
    color: ${z};
    pointer-events: none;
  }

  :host([disabled]:hover)::after {
    background-color: unset;
  }

  :host(:focus) {
    outline: none;
  }

  :host(:focus-visible) {
    border-radius: ${lt};
    box-shadow: 0 0 0 3px ${ue};
    outline: 1px solid ${Ri};
  }

  @media (forced-colors: active) {
    :host([aria-selected='true'])::after {
      background-color: Highlight;
    }
  }
`;function Xu(t={}){return m`
    <template slot="tab" role="tab">
      ${Ye(t)}
      <span class="tab-content"><slot></slot></span>
      ${Ke(t)}
    </template>
  `}var yc=Xu({});var xc={name:vc,registry:g.registry,styles:kc,template:yc};var vo=class extends y{disabledChanged(e,o){this.setDisabledSideEffect(o)}constructor(){super(),this.elementInternals=this.attachInternals(),this.elementInternals.role="tab"}connectedCallback(){super.connectedCallback(),this.slot||="tab",this.setDisabledSideEffect(this.disabled),this.styles&&this.$fastController.removeStyles(this.styles),this.styles=f`
      :host {
        --textContent: '${this.textContent}';
      }
    `,this.$fastController.addStyles(this.styles),Ze(this)}setDisabledSideEffect(e){e?this.setAttribute("aria-disabled","true"):this.removeAttribute("aria-disabled"),this.tabIndex=e&&this.getAttribute("aria-selected")!=="true"?-1:0}};i([l({mode:"boolean"})],vo.prototype,"disabled",void 0);Je(vo,Ce);var Sc={subtle:"subtle",transparent:"transparent"};var Wo=uo,Bc=`${g.prefix}-tablist`;var Nc=f`
  ${S("flex")}

  :host {
    --tabPaddingInline: ${Te};
    --tabPaddingBlock: ${ie};
    --tabIndicatorInsetInline: var(--tabPaddingInline);
    --tabIndicatorInsetBlock: 0;
    box-sizing: border-box;
    color: ${Re};
    flex-direction: row;
    position: relative;
  }

  :host([size='small']) {
    --tabPaddingBlock: ${Ro};
    --tabPaddingInline: ${We};
  }

  :host([size='large']) {
    --tabPaddingBlock: ${ro};
    --tabPaddingInline: ${Te};
  }

  :host([orientation='vertical']) {
    --tabPaddingBlock: ${te};
    --tabIndicatorInsetBlock: ${te};
    --_col-start-width: 0px;
    display: grid;
    grid-template-columns: ${Te} var(--_col-start-width) 1fr auto ${Te};
  }

  :host(:has([slot='start'])) {
    --_col-start-width: 24px;
  }

  @scope {
    :scope:has([slot='start']) {
      --_col-start-width: 24px;
    }
  }

  :host([orientation='vertical'][size='small']) {
    --tabPaddingBlock: ${Oo};
    --tabIndicatorInsetBlock: ${Ro};
  }

  :host([orientation='vertical'][size='large']) {
    --tabPaddingBlock: ${te};
    --tabIndicatorInsetBlock: ${oo};
  }

  ::slotted([slot='tab']) {
    padding-inline: var(--tabPaddingInline);
    padding-block: var(--tabPaddingBlock);
  }

  :host([orientation='vertical']) ::slotted([role='tab']) {
    justify-content: flex-start;
    display: grid;
    gap: 0;
    grid-column: 1 / -1;
    grid-template-columns: subgrid;
    grid-row: unset;
    padding-inline: 0;
  }

  :host ::slotted([slot='tab'])::after {
    height: ${ze};
    margin-block-start: auto;
  }

  :host([orientation='vertical']) ::slotted([slot='tab'])::after {
    width: ${ze};
    height: unset;
    margin-block-start: unset;
  }

  /* ::before adds a secondary indicator placeholder that appears right after click on the active tab */
  :host ::slotted([slot='tab'])::before {
    height: ${ze};
    border-radius: ${ee};
    content: '';
    inset-inline: var(--tabIndicatorInsetInline);
    inset-block: var(--tabIndicatorInsetBlock);
    position: absolute;
    margin-top: auto;
  }

  :host ::slotted([slot='tab'])::before {
    inset-inline: var(--tabIndicatorInsetInline);
    inset-block: var(--tabIndicatorInsetBlock);
  }

  :host ::slotted([slot='tab'][aria-selected='true'])::before {
    background-color: ${z};
  }

  :host ::slotted([slot='tab'][aria-selected='false']:hover)::after {
    height: ${ze};
    margin-block-start: auto;
    transform-origin: left;
  }

  :host([orientation='vertical']) ::slotted([slot='tab'])::before,
  :host([orientation='vertical']) ::slotted([slot='tab'][aria-selected='false']:hover)::after {
    height: unset;
    width: ${ze};
    margin-inline-end: auto;
    transform-origin: top;
  }

  :host([size='small']) ::slotted([slot='tab']) {
    font-size: ${j};
    line-height: ${Y};
  }

  :host([size='large']) ::slotted([slot='tab']) {
    font-size: ${pe};
    line-height: ${xe};
  }

  /* horizontal spacing for indicator */
  :host ::slotted([slot='tab'])::after,
  :host ::slotted([slot='tab'])::before,
  :host ::slotted([slot='tab']:hover)::after {
    inset-inline: var(--tabIndicatorInsetInline);
  }

  :host([orientation='vertical']) ::slotted([slot='tab'])::after,
  :host([orientation='vertical']) ::slotted([slot='tab'])::before,
  :host([orientation='vertical']) ::slotted([slot='tab']:hover)::after {
    inset-inline: 0;
    inset-block: var(--tabIndicatorInsetBlock);
  }

  /* disabled styles */
  :host([disabled]) {
    cursor: not-allowed;
    color: ${z};
  }

  :host([disabled]) ::slotted([slot='tab']) {
    pointer-events: none;
    cursor: not-allowed;
    color: ${z};
  }

  :host([disabled]) ::slotted([slot='tab']:after) {
    background-color: ${z};
  }

  :host([disabled]) ::slotted([slot='tab'][aria-selected='true'])::after {
    background-color: ${z};
  }

  :host([disabled]) ::slotted([slot='tab']:hover):before {
    content: unset;
  }

  :host([appearance='subtle']) ::slotted([slot='tab']:hover) {
    background-color: ${wr};
    color: ${Br};
    fill: ${xi};
  }

  :host([appearance='subtle']) ::slotted([slot='tab']:active) {
    background-color: ${Lo};
    fill: ${Lo};
    color: ${P};
  }

  /*
   * TODO: Remove '(text-size-adjust: auto)' after this bug is fixed:
   * https://bugs.webkit.org/show_bug.cgi?id=298646
   * Also remove the same trick from tab.styles.ts.
   * Using '@supports (text-size-adjust: auto)' here to exclude Safari 26.0 from
   * using CSS Anchor Positioning here because it crashes.
   */
  @supports (anchor-name: --a) and (text-size-adjust: auto) {
    ::slotted([slot='tab'][aria-selected='true']) {
      anchor-name: --tab;
    }

    :host::after {
      background-color: ${He};
      content: '';
      inline-size: 100%;
      inset-block: auto anchor(end);
      inset-inline: anchor(center) auto;
      position: fixed;
      position-anchor: --tab;
      transform: translateX(-50%);
      transition-property: inset-inline, width;
      transition-duration: ${ks};
      transition-timing-function: ${ys};
      z-index: 3;

      /* These styles should be in sync with tab.styles.ts’s :host::after */
      border-radius: ${ee};
      width: calc(anchor-size() - var(--tabIndicatorInsetInline) * 2);
      height: ${ze};
    }

    :host(:dir(rtl))::after {
      transform: translateX(50%);
    }

    :host([orientation='vertical'])::after {
      inset-block: anchor(center) auto;
      inset-inline: anchor(start) auto;
      transform: translateY(-50%);
      transition-property: inset-block, height;

      /* These styles should be in sync with #vertical-tab-highlight above */
      width: ${ze};
      height: calc(anchor-size() - var(--tabIndicatorInsetBlock) * 2);
    }

    :host([disabled])::after {
      background-color: ${z};
    }
  }
`;var $c=m`
  <template
    role="tablist"
    focusgroup="tablist inline block"
    @click="${(t,e)=>t.handleClick(e.event)}"
    @focusin="${(t,e)=>t.handleFocusIn(e.event)}"
  >
    <slot name="tab" ${oe("slottedTabs")}></slot>
  </template>
`;var Pc={name:Bc,registry:g.registry,styles:Nc,template:$c};var ft=class extends y{disabledChanged(e,o){this.elementInternals&&D(this.elementInternals,"disabled",o),this.setTabs({forceDisabled:!0})}orientationChanged(e,o){this.elementInternals&&(this.elementInternals.ariaOrientation=o??Wo.horizontal,De(this.elementInternals,e,o,Wo)),this.setTabs()}activeidChanged(e,o){this.tabs?.length>0&&this.changeTab(e,o)}slottedTabsChanged(e,o){this.tabs=o?.filter(r=>Yn(r))??[]}tabsChanged(e,o){this.tabs?.length>0&&this.setTabs({connectToPanel:!0})}setTabs({connectToPanel:e=!1,forceDisabled:o=!1}={}){if(!this.tabs)return;let r=this.getRootNode(),n="";for(let a of this.tabs){if(a.slot!=="tab")continue;a.id||=ut("tab-"),o?a.disabled=this.disabled:a.disabled=a.disabled||this.disabled,!n&&!a.disabled&&(n=a.id);let s=this.activeid===a.id;if(a.toggleAttribute("focusgroupstart",s),a.setAttribute("aria-selected",s.toString()),e){let d=a.getAttribute("aria-controls")??"",p=r.getElementById(d);d&&p&&(p.role??="tabpanel",p.hidden=this.activeid!==a.id,this.tabPanelMap.set(a,p))}}this.disabled||(this.activeid?this.changeTab(void 0,this.activeid):n&&(this.activeid=n))}handleFocusIn(e){this.activeid=e.target.id}handleClick(e){e.isTrusted||(this.activeid=e.target.id)}changeTab(e,o){let r=this.getRootNode(),n=e?r.getElementById(e):null,a=r.getElementById(o);if(!Yn(a)||a.disabled||!this.contains(a))return;if(n){n.setAttribute("aria-selected","false");let d=this.tabPanelMap.get(n);d&&(d.hidden=!0)}a.setAttribute("aria-selected","true");let s=this.tabPanelMap.get(a);s&&(s.hidden=!1),this.activetab=a,this.change()}constructor(){super(),this.elementInternals=this.attachInternals(),this.disabled=!1,this.orientation=Wo.horizontal,this.tabs=[],this.tabPanelMap=new WeakMap,this.change=()=>{this.$emit("change",this.activetab)},this.elementInternals.role="tablist",this.elementInternals.ariaOrientation=this.orientation??Wo.horizontal}connectedCallback(){super.connectedCallback(),Bs(this,()=>{this.setTabs()},{shallow:!0})}};i([l({mode:"boolean"})],ft.prototype,"disabled",void 0);i([l],ft.prototype,"orientation",void 0);i([l],ft.prototype,"activeid",void 0);i([T],ft.prototype,"slottedTabs",void 0);i([T],ft.prototype,"tabs",void 0);var ko=class extends ft{constructor(){super(...arguments),this.appearance=Sc.transparent}disconnectedCallback(){this.fg?.disconnect(),super.disconnectedCallback()}tabsChanged(e,o){super.tabsChanged(e,o),this.fgItems??=new fo(()=>this.tabs?.filter(r=>(r.getAttribute("aria-selected")==="true"||!r.disabled)&&!r.hidden)??[],()=>this.activetab??null),this.fg?this.fg.update():this.fg=new Vo(this,this.fgItems,{definition:{behavior:"tablist",axis:void 0,memory:!1,wrap:!0}})}};i([l],ko.prototype,"appearance",void 0);i([l],ko.prototype,"size",void 0);var en={outline:"outline",filledLighter:"filled-lighter",filledDarker:"filled-darker"},Ux=[en.filledLighter,en.filledDarker];var jo={none:"none",both:"both",horizontal:"horizontal",vertical:"vertical"},wc=`${g.prefix}-textarea`;var Fc=f`
  ${S("inline-block")}

  :host {
    /* typography */
    --font-size: ${j};
    --line-height: ${Y};

    /* layout */
    --padding-inline: ${Te};
    --padding-block: ${Ro};
    --min-block-size: 52px;
    --block-size: var(--min-block-size);
    --inline-size: 18rem;
    --border-width: ${M};
    --control-padding-inline: ${ne};

    /* colors */
    --color: ${P};
    --background-color: ${E};
    --border-color: ${Ve};
    --border-block-end-color: ${Fe};
    --placeholder-color: ${Nr};
    --focus-indicator-color: ${He};

    /* elevations */
    --box-shadow: none;

    /* others */
    --contain-size: size;
    --resize: none;

    color: var(--color);
    font-family: ${C};
    font-size: var(--font-size);
    font-weight: ${_};
    line-height: var(--line-height);
    position: relative;
  }

  :host(:hover) {
    --border-color: ${st};
    --border-block-end-color: ${Me};
  }

  :host(:active) {
    --border-color: ${Yt};
    --border-block-end-color: ${_e};
  }

  :host(:focus-within) {
    outline: none;
  }

  :host([block]:not([hidden])) {
    display: block;
  }

  :host([size='small']) {
    --font-size: ${R};
    --line-height: ${G};
    --min-block-size: 40px;
    --padding-block: ${zt};
    --padding-inline: ${We};
    --control-padding-inline: ${ne};
  }

  :host([size='large']) {
    --font-size: ${pe};
    --line-height: ${xe};
    --min-block-size: 64px;
    --padding-block: ${te};
    --padding-inline: ${ie};
    --control-padding-inline: ${We};
  }

  :host([resize='both']:not(${N})) {
    --resize: both;
  }

  :host([resize='horizontal']:not(${N})) {
    --resize: horizontal;
  }

  :host([resize='vertical']:not(${N})) {
    --resize: vertical;
  }

  :host([auto-resize]) {
    --block-size: auto;
    --contain-size: inline-size;
  }

  :host([appearance='filled-darker']) {
    --background-color: ${Ct};
    --border-color: var(--background-color);
    --border-block-end-color: var(--border-color);
  }

  :host([appearance='filled-lighter']) {
    --border-color: var(--background-color);
    --border-block-end-color: var(--border-color);
  }

  :host([appearance='filled-darker'][display-shadow]),
  :host([appearance='filled-lighter'][display-shadow]) {
    --box-shadow: ${to};
  }

  :host(${pl}) {
    --border-color: ${Eo};
    --border-block-end-color: ${Eo};
  }

  :host(${N}) {
    --color: ${z};
    --background-color: ${re};
    --border-color: ${Z};
    --border-block-end-color: var(--border-color);
    --box-shadow: none;
    --placeholder-color: ${z};

    cursor: no-drop;
    user-select: none;
  }

  .root {
    background-color: var(--background-color);
    border: var(--border-width) solid var(--border-color);
    border-block-end-color: var(--border-block-end-color);
    border-radius: ${A};
    box-sizing: border-box;
    box-shadow: var(--box-shadow);
    contain: paint layout style var(--contain-size);
    display: grid;
    grid-template: 1fr / 1fr;
    inline-size: var(--inline-size);
    min-block-size: var(--min-block-size);
    block-size: var(--block-size);
    overflow: hidden;
    padding: var(--padding-block) var(--padding-inline);
    position: relative;
    resize: var(--resize);
  }

  :host([block]) .root {
    inline-size: auto;
  }

  .root::after {
    border-bottom: 2px solid var(--focus-indicator-color);
    border-radius: 0 0 ${A} ${A};
    box-sizing: border-box;
    clip-path: inset(calc(100% - 2px) 1px 0px);
    content: '';
    height: max(2px, ${A});
    inset: auto -1px 0;
    position: absolute;
    transform: scaleX(0);
    transition-delay: ${io};
    transition-duration: ${Xr};
    transition-property: transform;
  }

  :host(:focus-within) .root::after {
    transform: scaleX(1);
    transition-property: transform;
    transition-duration: ${ao};
    transition-delay: ${so};
  }

  :host([readonly]) .root::after,
  :host(${N}) .root::after {
    content: none;
  }

  label {
    color: var(--color);
    display: flex;
    inline-size: fit-content;
    padding-block-end: ${zt};
    padding-inline-end: ${ae};
  }

  :host(:empty) label,
  label[hidden] {
    display: none;
  }

  .auto-sizer,
  .control {
    box-sizing: border-box;
    font: inherit;
    grid-column: 1 / -1;
    grid-row: 1 / -1;
    letter-space: inherit;
    padding: 0 var(--control-padding-inline);
  }

  .auto-sizer {
    display: none;
    padding-block-end: 2px; /* avoid scroll bar in Firefox */
    pointer-events: none;
    visibility: hidden;
    white-space: pre-wrap;
  }

  :host([auto-resize]) .auto-sizer {
    display: block;
  }

  .control {
    appearance: none;
    background-color: transparent;
    border: 0;
    color: inherit;
    field-sizing: content;
    max-block-size: 100%;
    outline: 0;
    overflow: auto;
    resize: none;
    text-align: inherit;
  }

  .control:disabled {
    cursor: inherit;
  }

  .control::placeholder {
    color: var(--placeholder-color);
  }

  ::selection {
    color: ${Ie};
    background-color: ${Pr};
  }

  @media (forced-colors: active) {
    :host {
      --border-color: FieldText;
      --border-block-end-color: FieldText;
      --focus-indicator-color: Highlight;
      --placeholder-color: FieldText;
    }

    :host(:hover),
    :host(:active),
    :host(:focus-within) {
      --border-color: Highlight;
      --border-block-end-color: Highlight;
    }

    :host(${N}) {
      --color: GrayText;
      --border-color: GrayText;
      --border-block-end-color: GrayText;
      --placeholder-color: GrayText;
    }
  }
`;function qu(){return m`
    <template>
      <label ${de("labelEl")} for="control" part="label">
        <slot name="label" ${oe("labelSlottedNodes")}></slot>
      </label>
      <div class="root" part="root" ${de("rootEl")}>
        <textarea
          ${de("controlEl")}
          id="control"
          class="control"
          part="control"
          ?required="${t=>t.required}"
          ?disabled="${t=>t.disabled}"
          ?readonly="${t=>t.readOnly}"
          ?spellcheck="${t=>t.spellcheck}"
          autocomplete="${t=>t.autocomplete}"
          maxlength="${t=>t.maxLength}"
          minlength="${t=>t.minLength}"
          placeholder="${t=>t.placeholder}"
          @change="${t=>t.handleControlChange()}"
          @select="${t=>t.handleControlSelect()}"
          @input="${t=>t.handleControlInput()}"
        ></textarea>
      </div>
      <div hidden>
        <slot ${oe("defaultSlottedNodes")}></slot>
      </div>
    </template>
  `}var Tc=qu();var Ac={name:wc,registry:g.registry,shadowOptions:{delegatesFocus:!0},styles:Fc,template:Tc};var Cc=t=>t.nodeType!==Node.TEXT_NODE||!!t.nodeValue?.trim().length;var X=class extends y{static{this.formAssociated=!0}controlElChanged(){this.controlElAttrObserver=new MutationObserver(()=>{this.setValidity()}),this.controlElAttrObserver.observe(this.controlEl,{attributes:!0,attributeFilter:["disabled","required","readonly","maxlength","minlength"]}),this.controlEl.addEventListener("input",()=>this.userInteracted=!0,{once:!0})}defaultSlottedNodesChanged(){O.enqueue(()=>{let e=this.getContent();this.defaultValue=e,this.value=e})}labelSlottedNodesChanged(){this.filteredLabelSlottedNodes=this.labelSlottedNodes.filter(Cc),this.labelEl&&(this.labelEl.hidden=!this.filteredLabelSlottedNodes.length),this.filteredLabelSlottedNodes.forEach(e=>{e.disabled=this.disabled,e.required=this.required})}autoResizeChanged(){this.maybeCreateAutoSizerEl(),D(this.elementInternals,"auto-resize",this.autoResize)}disabledChanged(){this.setDisabledSideEffect(this.disabled)}get form(){return this.elementInternals.form}get labels(){return this.elementInternals.labels}readOnlyChanged(){this.elementInternals&&(this.elementInternals.ariaReadOnly=`${!!this.readOnly}`,this.setValidity())}requiredChanged(){this.elementInternals.ariaRequired=`${!!this.required}`,this.filteredLabelSlottedNodes?.length&&this.filteredLabelSlottedNodes.forEach(e=>e.required=this.required)}resizeChanged(e,o){De(this.elementInternals,e,o,jo,"resize-"),D(this.elementInternals,"resize",qn(jo,o)&&o!==jo.none)}get textLength(){return this.controlEl.textLength}get type(){return"textarea"}get validity(){return this.elementInternals.validity}get validationMessage(){return this.elementInternals.validationMessage||this.controlEl.validationMessage}get willValidate(){return this.elementInternals.willValidate}get defaultValue(){return this.controlEl?.defaultValue??this.preConnectControlEl.defaultValue}set defaultValue(e){let o=this.controlEl??this.preConnectControlEl;o.defaultValue=e,this.controlEl&&!this.userInteracted&&(this.controlEl.value=e)}get value(){return this.controlEl?.value??this.preConnectControlEl.value}set value(e){let o=this.controlEl??this.preConnectControlEl;o.value=e,this.setFormValue(e),this.setValidity()}constructor(){super(),this.elementInternals=this.attachInternals(),this.filteredLabelSlottedNodes=[],this.labelSlottedNodes=[],this.userInteracted=!1,this.preConnectControlEl=document.createElement("textarea"),this.autoResize=!1,this.disabled=!1,this.displayShadow=!1,this.readOnly=!1,this.required=!1,this.resize=jo.none,this.spellcheck=!1}connectedCallback(){super.connectedCallback(),requestAnimationFrame(()=>{if(!this.$fastController.isConnected)return;let e=this.preConnectControlEl,o=this.getContent();this.defaultValue=o||e?.defaultValue||"",this.value=e?.value||this.defaultValue,this.setFormValue(this.value),this.setValidity(),this.preConnectControlEl=null,this.maybeCreateAutoSizerEl(),Ze(this)})}disconnectedCallback(){super.disconnectedCallback(),this.autoSizerObserver?.disconnect(),this.controlElAttrObserver?.disconnect()}formResetCallback(){this.value=this.defaultValue}formDisabledCallback(e){this.setDisabledSideEffect(e),this.setValidity()}setFormValue(e,o){this.elementInternals.setFormValue(e,e??o)}checkValidity(){return this.elementInternals.checkValidity()}reportValidity(){return this.elementInternals.reportValidity()}setCustomValidity(e){this.elementInternals?.setValidity({customError:!!e},e?e.toString():void 0),this.reportValidity()}setValidity(e,o,r){this.elementInternals&&(this.disabled||this.readOnly?this.elementInternals.setValidity({}):this.elementInternals.setValidity(e??this.controlEl?.validity,o??this.controlEl?.validationMessage,r??this.controlEl),this.userInteracted&&this.toggleUserValidityState())}select(){this.controlEl.select()}getContent(){return this.defaultSlottedNodes?.map(e=>{switch(e.nodeType){case Node.ELEMENT_NODE:return e.outerHTML;case Node.TEXT_NODE:return e.textContent.trim();default:return""}}).join("")||""}setDisabledSideEffect(e){this.elementInternals.ariaDisabled=`${e}`,this.controlEl&&(this.controlEl.disabled=e),this.filteredLabelSlottedNodes?.length&&this.filteredLabelSlottedNodes.forEach(o=>o.disabled=this.disabled)}toggleUserValidityState(){D(this.elementInternals,"user-invalid",!this.validity.valid),D(this.elementInternals,"user-valid",this.validity.valid)}maybeCreateAutoSizerEl(){if(!CSS.supports("field-sizing: content")){if(!this.autoResize){this.autoSizerEl?.remove(),this.autoSizerObserver?.disconnect();return}this.autoSizerEl||(this.autoSizerEl=document.createElement("div"),this.autoSizerEl.classList.add("auto-sizer"),this.autoSizerEl.ariaHidden="true"),this.rootEl?.prepend(this.autoSizerEl),this.autoSizerObserver||(this.autoSizerObserver=new ResizeObserver((e,o)=>{let r=window.getComputedStyle(this).writingMode.startsWith("horizontal")?"height":"width";this.style.getPropertyValue(r)!==""&&(this.autoSizerEl?.remove(),o.disconnect())})),this.autoSizerObserver.observe(this)}}handleControlInput(){this.autoResize&&this.autoSizerEl&&(this.autoSizerEl.textContent=this.value+" "),this.setFormValue(this.value),this.setValidity()}handleControlChange(){this.toggleUserValidityState(),this.$emit("change")}handleControlSelect(){this.$emit("select")}};i([T],X.prototype,"controlEl",void 0);i([T],X.prototype,"defaultSlottedNodes",void 0);i([T],X.prototype,"labelSlottedNodes",void 0);i([l],X.prototype,"autocomplete",void 0);i([l({attribute:"auto-resize",mode:"boolean"})],X.prototype,"autoResize",void 0);i([l({attribute:"dirname"})],X.prototype,"dirName",void 0);i([l({mode:"boolean"})],X.prototype,"disabled",void 0);i([l({attribute:"display-shadow",mode:"boolean"})],X.prototype,"displayShadow",void 0);i([l({attribute:"form"})],X.prototype,"initialForm",void 0);i([l({attribute:"maxlength",converter:ve})],X.prototype,"maxLength",void 0);i([l({attribute:"minlength",converter:ve})],X.prototype,"minLength",void 0);i([l],X.prototype,"name",void 0);i([l],X.prototype,"placeholder",void 0);i([l({attribute:"readonly",mode:"boolean"})],X.prototype,"readOnly",void 0);i([l({mode:"boolean"})],X.prototype,"required",void 0);i([l({mode:"fromView"})],X.prototype,"resize",void 0);i([l({mode:"boolean"})],X.prototype,"spellcheck",void 0);var Ot=class extends X{constructor(){super(...arguments),this.appearance=en.outline,this.block=!1}labelSlottedNodesChanged(){super.labelSlottedNodesChanged(),this.labelSlottedNodes.forEach(e=>{e.size=this.size})}handleChange(e,o){o==="size"&&this.labelSlottedNodes.forEach(r=>{r.size=this.size})}connectedCallback(){super.connectedCallback(),k.getNotifier(this).subscribe(this,"size")}disconnectedCallback(){super.disconnectedCallback(),k.getNotifier(this).unsubscribe(this,"size")}};i([l({mode:"fromView"})],Ot.prototype,"appearance",void 0);i([l({mode:"boolean"})],Ot.prototype,"block",void 0);i([l],Ot.prototype,"size",void 0);var Ic={email:"email",password:"password",tel:"tel",text:"text",url:"url"},Hc=["date","datetime-local","email","month","number","password","search","tel","text","time","url","week"],zc=`${g.prefix}-text-input`;var Dc=f`
  ${S("block")}

  :host {
    font-family: ${C};
    font-size: ${j};
    font-weight: ${_};
    line-height: ${Y};
    max-width: 400px;
  }
  .label {
    display: flex;
    color: ${P};
    padding-bottom: ${zt};
    flex-shrink: 0;
    padding-inline-end: ${ae};
  }

  .label[hidden],
  :host(:empty) .label {
    display: none;
  }

  .root {
    align-items: center;
    background-color: ${E};
    border: ${M} solid ${Ve};
    border-bottom-color: ${Fe};
    border-radius: ${A};
    box-sizing: border-box;
    height: 32px;
    display: inline-flex;
    flex-direction: row;
    gap: ${ne};
    padding: 0 ${Te};
    position: relative;
    width: 100%;
  }

  :has(.control:user-invalid) {
    border-color: ${Eo};
  }

  .root::after {
    box-sizing: border-box;
    content: '';
    position: absolute;
    left: -1px;
    bottom: 0px;
    right: -1px;
    height: max(2px, ${A});
    border-radius: 0 0 ${A} ${A};
    border-bottom: 2px solid ${He};
    clip-path: inset(calc(100% - 2px) 1px 0px);
    transform: scaleX(0);
    transition-property: transform;
    transition-duration: ${Xr};
    transition-delay: ${io};
  }
  .control {
    width: 100%;
    height: 100%;
    box-sizing: border-box;
    color: ${P};
    border-radius: ${A};
    background: ${re};
    font-family: ${C};
    font-weight: ${_};
    font-size: ${j};
    border: none;
    vertical-align: center;
  }
  .control:focus-visible {
    outline: 0;
    border: 0;
  }
  .control::placeholder {
    color: ${Nr};
  }
  :host ::slotted([slot='start']),
  :host ::slotted([slot='end']) {
    display: flex;
    align-items: center;
    justify-content: center;
    color: ${W};
    font-size: ${eo};
  }
  :host ::slotted([slot='start']) {
    padding-right: ${ne};
  }
  :host ::slotted([slot='end']) {
    padding-left: ${ne};
    gap: ${ae};
  }
  :host(:hover) .root {
    border-color: ${st};
    border-bottom-color: ${Me};
  }
  :host(:active) .root {
    border-color: ${Yt};
  }
  :host(:focus-within) .root {
    outline: transparent solid 2px;
    border-bottom: 0;
  }
  :host(:focus-within) .root::after {
    transform: scaleX(1);
    transition-property: transform;
    transition-duration: ${ao};
    transition-delay: ${so};
  }
  :host(:focus-within:active) .root:after {
    border-bottom-color: ${Cr};
  }
  :host([appearance='outline']:focus-within) .root {
    border: ${M} solid ${Ve};
  }
  :host(:focus-within) .control {
    color: ${P};
  }
  :host(${N}) .root {
    background: ${re};
    border: ${M} solid ${Z};
  }
  :host(${N}) .control::placeholder,
  :host(${N}) ::slotted([slot='start']),
  :host(${N}) ::slotted([slot='end']) {
    color: ${z};
  }
  ::selection {
    color: ${Ie};
    background-color: ${Pr};
  }
  :host([control-size='small']) .control {
    font-size: ${R};
    font-weight: ${_};
    line-height: ${G};
  }
  :host([control-size='small']) .root {
    height: 24px;
    gap: ${ne};
    padding: 0 ${We};
  }
  :host([control-size='small']) ::slotted([slot='start']),
  :host([control-size='small']) ::slotted([slot='end']) {
    font-size: ${pe};
  }
  :host([control-size='large']) .control {
    font-size: ${pe};
    font-weight: ${_};
    line-height: ${xe};
  }
  :host([control-size='large']) .root {
    height: 40px;
    gap: ${Qe};
    padding: 0 ${ie};
  }
  :host([control-size='large']) ::slotted([slot='start']),
  :host([control-size='large']) ::slotted([slot='end']) {
    font-size: ${Vr};
  }
  :host([appearance='underline']) .root {
    background: ${re};
    border: 0;
    border-radius: 0;
    border-bottom: ${M} solid ${Fe};
  }
  :host([appearance='underline']:hover) .root {
    border-bottom-color: ${Me};
  }
  :host([appearance='underline']:active) .root {
    border-bottom-color: ${_e};
  }
  :host([appearance='underline']:focus-within) .root {
    border: 0;
    border-bottom-color: ${_e};
  }
  :host([appearance='underline']${N}) .root {
    border-bottom-color: ${Z};
  }
  :host([appearance='filled-lighter']) .root,
  :host([appearance='filled-darker']) .root {
    border: ${M} solid ${K};
    box-shadow: ${to};
  }
  :host([appearance='filled-lighter']) .root {
    background: ${E};
  }
  :host([appearance='filled-darker']) .root {
    background: ${Ct};
  }
  :host([appearance='filled-lighter']:hover) .root,
  :host([appearance='filled-darker']:hover) .root {
    border-color: ${Mn};
  }
  :host([appearance='filled-lighter']:active) .root,
  :host([appearance='filled-darker']:active) .root {
    border-color: ${Mn};
    background: ${Ct};
  }
`;function Uu(t={}){return m`
    <template @keydown="${(e,o)=>e.keydownHandler(o.event)}">
      <label part="label" for="control" class="label" ${de("controlLabel")}>
        <slot ${oe("defaultSlottedNodes")}></slot>
      </label>
      <div class="root" part="root">
        ${Ye(t)}
        <input
          class="control"
          part="control"
          id="control"
          @change="${(e,o)=>e.changeHandler(o.event)}"
          @input="${(e,o)=>e.inputHandler(o.event)}"
          autocomplete="${e=>e.autocomplete}"
          ?disabled="${e=>e.disabled}"
          list="${e=>e.list}"
          maxlength="${e=>e.maxlength}"
          minlength="${e=>e.minlength}"
          ?multiple="${e=>e.multiple}"
          name="${e=>e.name}"
          pattern="${e=>e.pattern}"
          placeholder="${e=>e.placeholder}"
          ?readonly="${e=>e.readOnly}"
          ?required="${e=>e.required}"
          size="${e=>e.size}"
          spellcheck="${e=>e.spellcheck}"
          type="${e=>e.type}"
          value="${e=>e.value}"
          ${de("control")}
        />
        ${Ke(t)}
      </div>
    </template>
  `}var Lc=Uu();var Ec={name:zc,registry:g.registry,shadowOptions:{delegatesFocus:!0},styles:Dc,template:Lc};var L=class extends y{constructor(){super(...arguments),this.type=Ic.text,this.dirtyValue=!1,this.committedValue="",this.isUserInput=!1,this.elementInternals=this.attachInternals()}currentValueChanged(e,o){this.value=o}defaultSlottedNodesChanged(e,o){O.enqueue(()=>{this.controlLabel&&(this.controlLabel.hidden=!o?.some(r=>r.nodeType===Node.ELEMENT_NODE||r.nodeType===Node.TEXT_NODE&&!!r.textContent?.trim()))})}initialValueChanged(){this.dirtyValue||(this.value=this.initialValue)}readOnlyChanged(){this.$fastController.isConnected&&(this.elementInternals.ariaReadOnly=`${!!this.readOnly}`)}requiredChanged(e,o){this.$fastController.isConnected&&(this.elementInternals.ariaRequired=`${!!o}`)}controlChanged(e,o){O.enqueue(()=>{this.$fastController.isConnected&&this.setValidity()})}static{this.formAssociated=!0}get validity(){return this.elementInternals.validity}get validationMessage(){return this.elementInternals.validationMessage||this.control.validationMessage}get value(){return k.track(this,"value"),this.currentValue}set value(e){this.currentValue=e,this.isUserInput||(this.committedValue=e??""),this.elementInternals&&this.control&&(this.control.value=e??"",this.setFormValue(e),this.setValidity(),k.notify(this,"value"))}get willValidate(){return this.elementInternals.willValidate}get form(){return this.elementInternals.form}changeHandler(e){return this.control.value!==this.committedValue&&this.emitChange(e),!0}emitChange(e){this.committedValue=this.control.value,this.setValidity(),this.$emit("change",e,{bubbles:!0,composed:!0})}checkValidity(){return this.elementInternals.checkValidity()}clickHandler(e){return e.target===this&&this.control?.click(),!0}connectedCallback(){super.connectedCallback(),this.setFormValue(this.value),this.setValidity(),Ze(this)}formResetCallback(){this.value=this.initialValue,this.dirtyValue=!1}implicitSubmit(){if(!this.elementInternals.form)return;if(this.elementInternals.form.elements.length===1){this.elementInternals.form.requestSubmit();return}let e=[...this.elementInternals.form.elements],o=e.find(n=>n.getAttribute("type")==="submit");if(o){o.click();return}e.filter(n=>Hc.includes(n.getAttribute("type")??"")).length>1||this.elementInternals.form.requestSubmit()}inputHandler(e){this.dirtyValue=!0,this.isUserInput=!0;try{this.value=this.control.value}finally{this.isUserInput=!1}return!0}keydownHandler(e){return e.key!=="Enter"||e.isComposing||e.keyCode===229||e.composedPath()[0]!==this.control||(this.control.value!==this.committedValue&&this.emitChange(),this.implicitSubmit()),!0}select(){this.control.select(),this.$emit("select")}setCustomValidity(e){this.elementInternals.setValidity({customError:!0},e),this.reportValidity()}reportValidity(){return this.elementInternals.reportValidity()}setFormValue(e,o){this.elementInternals?.setFormValue(e,e??o)}setValidity(e,o,r){if(this.elementInternals&&this.control){if(this.disabled){this.elementInternals.setValidity({});return}this.elementInternals.setValidity(e??this.control.validity,o??this.validationMessage,r??this.control)}}};i([l],L.prototype,"autocomplete",void 0);i([l({attribute:"current-value"})],L.prototype,"currentValue",void 0);i([T],L.prototype,"defaultSlottedNodes",void 0);i([l],L.prototype,"dirname",void 0);i([l({mode:"boolean"})],L.prototype,"disabled",void 0);i([l({attribute:"form"})],L.prototype,"formAttribute",void 0);i([l({attribute:"value",mode:"fromView"})],L.prototype,"initialValue",void 0);i([l],L.prototype,"list",void 0);i([l({converter:ve})],L.prototype,"maxlength",void 0);i([l({converter:ve})],L.prototype,"minlength",void 0);i([l({mode:"boolean"})],L.prototype,"multiple",void 0);i([l],L.prototype,"name",void 0);i([l],L.prototype,"pattern",void 0);i([l],L.prototype,"placeholder",void 0);i([l({attribute:"readonly",mode:"boolean"})],L.prototype,"readOnly",void 0);i([l({mode:"boolean"})],L.prototype,"required",void 0);i([l({converter:ve})],L.prototype,"size",void 0);i([l({converter:{fromView:t=>typeof t=="string"?["true",""].includes(t.trim().toLowerCase()):null,toView:t=>t.toString()}})],L.prototype,"spellcheck",void 0);i([l],L.prototype,"type",void 0);i([T],L.prototype,"control",void 0);i([T],L.prototype,"controlLabel",void 0);var Rt=class extends L{};i([l],Rt.prototype,"appearance",void 0);i([l({attribute:"control-size"})],Rt.prototype,"controlSize",void 0);Je(Rt,Ce);var Pe=class extends y{constructor(){super(...arguments),this.elementInternals=this.attachInternals(),this.nowrap=!1,this.truncate=!1,this.italic=!1,this.underline=!1,this.strikethrough=!1,this.block=!1}};i([l({mode:"boolean"})],Pe.prototype,"nowrap",void 0);i([l({mode:"boolean"})],Pe.prototype,"truncate",void 0);i([l({mode:"boolean"})],Pe.prototype,"italic",void 0);i([l({mode:"boolean"})],Pe.prototype,"underline",void 0);i([l({mode:"boolean"})],Pe.prototype,"strikethrough",void 0);i([l({mode:"boolean"})],Pe.prototype,"block",void 0);i([l],Pe.prototype,"size",void 0);i([l],Pe.prototype,"font",void 0);i([l],Pe.prototype,"weight",void 0);i([l],Pe.prototype,"align",void 0);var Oc=`${g.prefix}-text`;var Rc=m`<slot></slot>`;var Mc=f`
  ${S("inline")}

  :host {
    font-family: ${C};
    font-size: ${j};
    line-height: ${Y};
    font-weight: ${_};
    text-align: start;
  }

  :host([nowrap]),
  :host([nowrap]) ::slotted(*) {
    white-space: nowrap;
    overflow: hidden;
  }
  :host([truncate]),
  :host([truncate]) ::slotted(*) {
    text-overflow: ellipsis;
  }
  :host([block]) {
    display: block;
  }
  :host([italic]) {
    font-style: italic;
  }
  :host([underline]) {
    text-decoration-line: underline;
  }
  :host([strikethrough]) {
    text-decoration-line: line-through;
  }
  :host([underline][strikethrough]) {
    text-decoration-line: line-through underline;
  }
  :host([size='100']) {
    font-size: ${_r};
    line-height: ${Wr};
  }
  :host([size='200']) {
    font-size: ${R};
    line-height: ${G};
  }
  :host([size='400']) {
    font-size: ${pe};
    line-height: ${xe};
  }
  :host([size='500']) {
    font-size: ${eo};
    line-height: ${jr};
  }
  :host([size='600']) {
    font-size: ${Vr};
    line-height: ${ss};
  }
  :host([size='700']) {
    font-size: ${ts};
    line-height: ${ls};
  }
  :host([size='800']) {
    font-size: ${os};
    line-height: ${cs};
  }
  :host([size='900']) {
    font-size: ${rs};
    line-height: ${ds};
  }
  :host([size='1000']) {
    font-size: ${ns};
    line-height: ${us};
  }
  :host([font='monospace']) {
    font-family: ${Zi};
  }
  :host([font='numeric']) {
    font-family: ${es};
  }
  :host([weight='medium']) {
    font-weight: ${as};
  }
  :host([weight='semibold']) {
    font-weight: ${he};
  }
  :host([weight='bold']) {
    font-weight: ${is};
  }
  :host([align='center']) {
    text-align: center;
  }
  :host([align='end']) {
    text-align: end;
  }
  :host([align='justify']) {
    text-align: justify;
  }

  ::slotted(*) {
    font: inherit;
    line-height: inherit;
    text-decoration-line: inherit;
    text-align: inherit;
    text-decoration-line: inherit;
    margin: 0;
  }
`;var _c={name:Oc,registry:g.registry,styles:Mc,template:Rc};var Ku="adoptedStyleSheets"in document,Yu="CSSScopeRule"in window,Jn=new Map,Qn=new Map,Xo=new Map,Zn=new Map,Go=new CSSStyleSheet;function tn(t,e=document){if(!(!e||!tp(e))){if(!Ku||e instanceof HTMLElement&&!e.shadowRoot&&!Yu){let o=e===document?document.documentElement:e;ip(t,o);return}[document,document.documentElement,document.body].includes(e)?op(t):rp(t,e)}}var Ju=/^[a-zA-Z_-][a-zA-Z0-9_-]*$/;function Qu(t){return Ju.test(t)?t:""}var Zu=/(;|{|}|\/\*|\*\/|@import|url\s*\(|expression\s*\(|javascript:)/i;function ep(t){return Zu.test(t)?"":t}function ea(t){return Jn.has(t)||Jn.set(t,Object.keys(t).reduce((e,o)=>{let r=Qu(o),n=ep(t[o].toString());return r&&n?`${e}--${r}:${n};`:e},"")),Jn.get(t)}function tp(t){return[document,document.documentElement].includes(t)||t instanceof HTMLElement&&!!t.closest("body")}function op(t){if(t===null){document.adoptedStyleSheets.includes(Go)&&Go.replaceSync("");return}Go.replaceSync(`
    html {
      ${ea(t)}
    }
  `),document.adoptedStyleSheets.includes(Go)||document.adoptedStyleSheets.push(Go)}function rp(t,e){if(t===null){e.shadowRoot&&Xo.has(e)?Xo.get(e).replaceSync(""):(delete e.dataset.fluentTheme,Vc(e));return}e.shadowRoot?np(e).replaceSync(`
      :host {
        ${ea(t)}
      }
    `):(e.dataset.fluentTheme=ap(t),Vc(e))}function np(t){if(!Xo.has(t)){let e=new CSSStyleSheet;Xo.set(t,e),t.shadowRoot?.adoptedStyleSheets.push(e)}return Xo.get(t)}function ap(t){if(!Qn.has(t)){let e=ut("fluent-theme-"),o=new CSSStyleSheet;Qn.set(t,e),o.replaceSync(`
      @scope ([data-fluent-theme="${e}"]) {
        :scope {
          ${ea(t)}
        }
      }
    `),document.adoptedStyleSheets.push(o)}return Qn.get(t)}function ip(t,e){let o;if(t===null){if(!Zn.has(e))return;o=Zn.get(e)}else Zn.set(e,t),o=t;for(let[r,n]of Object.entries(o))t===null?e.style.removeProperty(`--${r}`):e.style.setProperty(`--${r}`,n.toString())}var{userAgent:sp}=navigator,lp=/\bAppleWebKit\/[\d+\.]+\b/.test(sp);function Vc(t){if(!lp)return;let e="visibility",o="hidden",r=t.style.getPropertyValue(e);t.style.setProperty(e,o),O.process(),t.style.setProperty(e,r)}var Wc=`${g.prefix}-tooltip`;var jc=f`
  ${S("inline-flex")}

  :host(:not(:popover-open)) {
    display: none;
  }

  :host {
    --position-area: block-start;
    --position-try-options: flip-block;
    --block-offset: ${zt};
    --inline-offset: ${ae};
    background: ${E};
    border-radius: ${A};
    border: 1px solid ${K};
    box-sizing: border-box;
    color: ${P};
    display: inline-flex;
    filter: drop-shadow(0 0 2px ${Mi}) drop-shadow(0 4px 8px ${_i});
    font-family: ${C};
    font-size: ${R};
    inset: unset;
    line-height: ${G};
    margin: unset; /* Remove browser default for [popover] */
    max-width: 240px;
    overflow: visible;
    padding: 4px ${Te} 6px;
    position: absolute;
    position-area: var(--position-area);
    position-try-fallbacks: var(--position-try-options);
    width: auto;
    z-index: 1;
  }

  @supports (inset-area: block-start) {
    :host {
      inset-area: var(--position-area);
      position-try-fallbacks: var(--position-try-options);
    }
  }

  :host(:is([positioning^='above'], [positioning^='below'], :not([positioning]))) {
    margin-block: var(--block-offset);
  }

  :host(:is([positioning^='before'], [positioning^='after'])) {
    margin-inline: var(--inline-offset);
    --position-try-options: flip-inline;
  }

  :host([positioning='above-start']) {
    --position-area: block-start span-inline-end;
  }
  :host([positioning='above']) {
    --position-area: block-start;
  }
  :host([positioning='above-end']) {
    --position-area: block-start span-inline-start;
  }
  :host([positioning='below-start']) {
    --position-area: block-end span-inline-end;
  }
  :host([positioning='below']) {
    --position-area: block-end;
  }
  :host([positioning='below-end']) {
    --position-area: block-end span-inline-start;
  }
  :host([positioning='before-top']) {
    --position-area: inline-start span-block-end;
  }
  :host([positioning='before']) {
    --position-area: inline-start;
  }
  :host([positioning='before-bottom']) {
    --position-area: inline-start span-block-start;
  }
  :host([positioning='after-top']) {
    --position-area: inline-end span-block-end;
  }
  :host([positioning='after']) {
    --position-area: inline-end;
  }
  :host([positioning='after-bottom']) {
    --position-area: inline-end span-block-start;
  }
`;var Gc=m`
  <template popover aria-hidden="true">
    <slot></slot>
  </template>
`;var Xc={name:Wc,registry:g.registry,styles:jc,template:Gc};var $t=class extends y{positioningChanged(){this.setFallbackStyles()}get anchorElement(){let e=this.getRootNode();return(e instanceof ShadowRoot?e:document).getElementById(this.anchor??"")}constructor(){super(),this.elementInternals=this.attachInternals(),this.id=ut("tooltip-"),this.defaultDelay=250,this.anchor="",this.anchorPositioningStyleElement=null,this.mouseenterAnchorHandler=()=>this.showTooltip(this.delay),this.mouseleaveAnchorHandler=()=>this.hideTooltip(this.delay),this.focusAnchorHandler=()=>this.showTooltip(0),this.blurAnchorHandler=()=>this.hideTooltip(0),this.anchorPositioningReady=!1,this.elementInternals.role="tooltip"}connectedCallback(){if(super.connectedCallback(),this.popover??="auto",!this.anchorElement)return;let e=this.anchorElement.style.anchorName||`--${this.anchor}`,o=this.anchorElement.getAttribute("aria-describedby");if(this.anchorElement.setAttribute("aria-describedby",o?`${o} ${this.id}`:this.id),this.anchorElement.addEventListener("focus",this.focusAnchorHandler),this.anchorElement.addEventListener("blur",this.blurAnchorHandler),this.anchorElement.addEventListener("mouseenter",this.mouseenterAnchorHandler),this.anchorElement.addEventListener("mouseleave",this.mouseleaveAnchorHandler),jn){Zs||(this.anchorElement.style.anchorName=e,this.style.positionAnchor=e);return}O.enqueue(()=>this.setFallbackStyles())}disconnectedCallback(){if(this.anchorElement?.removeEventListener("focus",this.focusAnchorHandler),this.anchorElement?.removeEventListener("blur",this.blurAnchorHandler),this.anchorElement?.removeEventListener("mouseenter",this.mouseenterAnchorHandler),this.anchorElement?.removeEventListener("mouseleave",this.mouseleaveAnchorHandler),this.anchorElement){let o=(this.anchorElement.getAttribute("aria-describedby")??"").trim().split(/\s+/).filter(r=>r!==this.id);o.length?this.anchorElement.setAttribute("aria-describedby",o.join(" ")):this.anchorElement.removeAttribute("aria-describedby")}super.disconnectedCallback()}showTooltip(e=this.defaultDelay){if(!this.anchorPositioningReady){this.setFallbackStyles().then(()=>{this.showTooltip(e)});return}setTimeout(()=>{this.setAttribute("aria-hidden","false"),this.showPopover()},e)}hideTooltip(e=this.defaultDelay){setTimeout(()=>{if(this.matches(":hover")||this.anchorElement?.matches(":hover")){this.hideTooltip(e);return}this.setAttribute("aria-hidden","true"),this.hidePopover()},e)}async setFallbackStyles(){if(jn){this.anchorPositioningReady=!0;return}if(!this.anchorElement)return;let e=this.anchorElement.style.anchorName||`--${this.anchor}`;this.anchorPositioningStyleElement||(this.anchorPositioningStyleElement=document.createElement("style"),document.head.append(this.anchorPositioningStyleElement));let[o,r]=this.positioning?.split("-")??[];r||((o==="above"||o==="below")&&(r="centerX"),(o==="before"||o==="after")&&(r="centerY"));let n={above:"bottom: anchor(top);",below:"top: anchor(bottom);",before:"right: anchor(left);",after:"left: anchor(right);"},a=n[o]??n.above,s={start:"left: anchor(left);",end:"right: anchor(right);",top:"top: anchor(top);",bottom:"bottom: anchor(bottom);",centerX:"left: anchor(center); translate: -50% 0;",centerY:"top: anchor(center); translate: 0 -50%;"},d=s[r]??s.centerX;this.anchorPositioningStyleElement.textContent=`
      #${this.anchor} {
        anchor-name: ${e};
      }
      #${this.id} {
        inset: unset;
        position-anchor: ${e};
        position: fixed;
        ${a}
        ${d}
      }
    `,window.CSS_ANCHOR_POLYFILL&&(await window.CSS_ANCHOR_POLYFILL({elements:[this.anchorPositioningStyleElement]}),this.anchorPositioningReady=!0)}};i([l],$t.prototype,"id",void 0);i([l({converter:ve})],$t.prototype,"delay",void 0);i([l],$t.prototype,"positioning",void 0);i([l],$t.prototype,"anchor",void 0);var c={2:"#050505",4:"#0a0a0a",6:"#0f0f0f",8:"#141414",10:"#1a1a1a",12:"#1f1f1f",14:"#242424",16:"#292929",18:"#2e2e2e",20:"#333333",22:"#383838",24:"#3d3d3d",26:"#424242",28:"#474747",30:"#4d4d4d",32:"#525252",34:"#575757",36:"#5c5c5c",38:"#616161",40:"#666666",42:"#6b6b6b",44:"#707070",46:"#757575",48:"#7a7a7a",50:"#808080",52:"#858585",54:"#8a8a8a",56:"#8f8f8f",58:"#949494",60:"#999999",62:"#9e9e9e",64:"#a3a3a3",66:"#a8a8a8",68:"#adadad",70:"#b3b3b3",72:"#b8b8b8",74:"#bdbdbd",76:"#c2c2c2",78:"#c7c7c7",80:"#cccccc",82:"#d1d1d1",84:"#d6d6d6",86:"#dbdbdb",88:"#e0e0e0",90:"#e6e6e6",92:"#ebebeb",94:"#f0f0f0",96:"#f5f5f5",98:"#fafafa",99:"#fcfcfc"},J={5:"rgba(255, 255, 255, 0.05)",10:"rgba(255, 255, 255, 0.1)",20:"rgba(255, 255, 255, 0.2)",30:"rgba(255, 255, 255, 0.3)",40:"rgba(255, 255, 255, 0.4)",50:"rgba(255, 255, 255, 0.5)",60:"rgba(255, 255, 255, 0.6)",70:"rgba(255, 255, 255, 0.7)",80:"rgba(255, 255, 255, 0.8)",90:"rgba(255, 255, 255, 0.9)"},we={5:"rgba(0, 0, 0, 0.05)",10:"rgba(0, 0, 0, 0.1)",20:"rgba(0, 0, 0, 0.2)",30:"rgba(0, 0, 0, 0.3)",40:"rgba(0, 0, 0, 0.4)",50:"rgba(0, 0, 0, 0.5)",60:"rgba(0, 0, 0, 0.6)",70:"rgba(0, 0, 0, 0.7)",80:"rgba(0, 0, 0, 0.8)",90:"rgba(0, 0, 0, 0.9)"},qc={5:"rgba(26, 26, 26, 0.05)",10:"rgba(26, 26, 26, 0.1)",20:"rgba(26, 26, 26, 0.2)",30:"rgba(26, 26, 26, 0.3)",40:"rgba(26, 26, 26, 0.4)",50:"rgba(26, 26, 26, 0.5)",60:"rgba(26, 26, 26, 0.6)",70:"rgba(26, 26, 26, 0.7)",80:"rgba(26, 26, 26, 0.8)",90:"rgba(26, 26, 26, 0.9)"},Uc={5:"rgba(31, 31, 31, 0.05)",10:"rgba(31, 31, 31, 0.1)",20:"rgba(31, 31, 31, 0.2)",30:"rgba(31, 31, 31, 0.3)",40:"rgba(31, 31, 31, 0.4)",50:"rgba(31, 31, 31, 0.5)",60:"rgba(31, 31, 31, 0.6)",70:"rgba(31, 31, 31, 0.7)",80:"rgba(31, 31, 31, 0.8)",90:"rgba(31, 31, 31, 0.9)"},ta={5:"rgba(36, 36, 36, 0.05)",10:"rgba(36, 36, 36, 0.1)",20:"rgba(36, 36, 36, 0.2)",30:"rgba(36, 36, 36, 0.3)",40:"rgba(36, 36, 36, 0.4)",50:"rgba(36, 36, 36, 0.5)",60:"rgba(36, 36, 36, 0.6)",70:"rgba(36, 36, 36, 0.7)",80:"rgba(36, 36, 36, 0.8)",90:"rgba(36, 36, 36, 0.9)"},b="#ffffff",yo="#000000";var Kc={shade50:"#130204",shade40:"#230308",shade30:"#420610",shade20:"#590815",shade10:"#690a19",primary:"#750b1c",tint10:"#861b2c",tint20:"#962f3f",tint30:"#ac4f5e",tint40:"#d69ca5",tint50:"#e9c7cd",tint60:"#f9f0f2"};var oa={shade50:"#200205",shade40:"#3b0509",shade30:"#6e0811",shade20:"#960b18",shade10:"#b10e1c",primary:"#c50f1f",tint10:"#cc2635",tint20:"#d33f4c",tint30:"#dc626d",tint40:"#eeacb2",tint50:"#f6d1d5",tint60:"#fdf3f4"},Yc={shade50:"#210809",shade40:"#3f1011",shade30:"#751d1f",shade20:"#9f282b",shade10:"#bc2f32",primary:"#d13438",tint10:"#d7494c",tint20:"#dc5e62",tint30:"#e37d80",tint40:"#f1bbbc",tint50:"#f8dadb",tint60:"#fdf6f6"},Jc={shade50:"#230900",shade40:"#411200",shade30:"#7a2101",shade20:"#a62d01",shade10:"#c43501",primary:"#da3b01",tint10:"#de501c",tint20:"#e36537",tint30:"#e9835e",tint40:"#f4bfab",tint50:"#f9dcd1",tint60:"#fdf6f3"};var Qc={shade50:"#200d03",shade40:"#3d1805",shade30:"#712d09",shade20:"#9a3d0c",shade10:"#b6480e",primary:"#ca5010",tint10:"#d06228",tint20:"#d77440",tint30:"#df8e64",tint40:"#efc4ad",tint50:"#f7dfd2",tint60:"#fdf7f4"},Zc={shade50:"#271002",shade40:"#4a1e04",shade30:"#8a3707",shade20:"#bc4b09",shade10:"#de590b",primary:"#f7630c",tint10:"#f87528",tint20:"#f98845",tint30:"#faa06b",tint40:"#fdcfb4",tint50:"#fee5d7",tint60:"#fff9f5"},ed={shade50:"#291600",shade40:"#4d2a00",shade30:"#8f4e00",shade20:"#c26a00",shade10:"#e67e00",primary:"#ff8c00",tint10:"#ff9a1f",tint20:"#ffa83d",tint30:"#ffba66",tint40:"#ffddb3",tint50:"#ffedd6",tint60:"#fffaf5"},td={shade50:"#251a00",shade40:"#463100",shade30:"#835b00",shade20:"#b27c00",shade10:"#d39300",primary:"#eaa300",tint10:"#edad1c",tint20:"#efb839",tint30:"#f2c661",tint40:"#f9e2ae",tint50:"#fcefd3",tint60:"#fefbf4"},od={shade50:"#282400",shade40:"#4c4400",shade30:"#817400",shade20:"#c0ad00",shade10:"#e4cc00",primary:"#fde300",tint10:"#fde61e",tint20:"#fdea3d",tint30:"#feee66",tint40:"#fef7b2",tint50:"#fffad6",tint60:"#fffef5"},rd={shade50:"#1f1900",shade40:"#3a2f00",shade30:"#6c5700",shade20:"#937700",shade10:"#ae8c00",primary:"#c19c00",tint10:"#c8a718",tint20:"#d0b232",tint30:"#dac157",tint40:"#ecdfa5",tint50:"#f5eece",tint60:"#fdfbf2"},nd={shade50:"#181202",shade40:"#2e2103",shade30:"#553e06",shade20:"#745408",shade10:"#89640a",primary:"#986f0b",tint10:"#a47d1e",tint20:"#b18c34",tint30:"#c1a256",tint40:"#e0cea2",tint50:"#efe4cb",tint60:"#fbf8f2"},ad={shade50:"#170e07",shade40:"#2b1a0e",shade30:"#50301a",shade20:"#6c4123",shade10:"#804d29",primary:"#8e562e",tint10:"#9c663f",tint20:"#a97652",tint30:"#bb8f6f",tint40:"#ddc3b0",tint50:"#edded3",tint60:"#faf7f4"};var id={shade50:"#0c1501",shade40:"#162702",shade30:"#294903",shade20:"#376304",shade10:"#427505",primary:"#498205",tint10:"#599116",tint20:"#6ba02b",tint30:"#85b44c",tint40:"#bdd99b",tint50:"#dbebc7",tint60:"#f6faf0"},sd={shade50:"#002111",shade40:"#003d20",shade30:"#00723b",shade20:"#009b51",shade10:"#00b85f",primary:"#00cc6a",tint10:"#19d279",tint20:"#34d889",tint30:"#5ae0a0",tint40:"#a8f0cd",tint50:"#cff7e4",tint60:"#f3fdf8"},ld={shade50:"#031a02",shade40:"#063004",shade30:"#0b5a08",shade20:"#0e7a0b",shade10:"#11910d",primary:"#13a10e",tint10:"#27ac22",tint20:"#3db838",tint30:"#5ec75a",tint40:"#a7e3a5",tint50:"#cef0cd",tint60:"#f2fbf2"},ra={shade50:"#031403",shade40:"#052505",shade30:"#094509",shade20:"#0c5e0c",shade10:"#0e700e",primary:"#107c10",tint10:"#218c21",tint20:"#359b35",tint30:"#54b054",tint40:"#9fd89f",tint50:"#c9eac9",tint60:"#f1faf1"},cd={shade50:"#021102",shade40:"#032003",shade30:"#063b06",shade20:"#085108",shade10:"#0a5f0a",primary:"#0b6a0b",tint10:"#1a7c1a",tint20:"#2d8e2d",tint30:"#4da64d",tint40:"#9ad29a",tint50:"#c6e7c6",tint60:"#f0f9f0"},dd={shade50:"#001d1f",shade40:"#00373a",shade30:"#00666d",shade20:"#008b94",shade10:"#00a5af",primary:"#00b7c3",tint10:"#18bfca",tint20:"#32c8d1",tint30:"#58d3db",tint40:"#a6e9ed",tint50:"#cef3f5",tint60:"#f2fcfd"},ud={shade50:"#001516",shade40:"#012728",shade30:"#02494c",shade20:"#026467",shade10:"#037679",primary:"#038387",tint10:"#159195",tint20:"#2aa0a4",tint30:"#4cb4b7",tint40:"#9bd9db",tint50:"#c7ebec",tint60:"#f0fafa"};var pd={shade50:"#000f12",shade40:"#001b22",shade30:"#00333f",shade20:"#004555",shade10:"#005265",primary:"#005b70",tint10:"#0f6c81",tint20:"#237d92",tint30:"#4496a9",tint40:"#94c8d4",tint50:"#c3e1e8",tint60:"#eff7f9"};var hd={shade50:"#001322",shade40:"#002440",shade30:"#004377",shade20:"#005ba1",shade10:"#006cbf",primary:"#0078d4",tint10:"#1a86d9",tint20:"#3595de",tint30:"#5caae5",tint40:"#a9d3f2",tint50:"#d0e7f8",tint60:"#f3f9fd"},gd={shade50:"#000c16",shade40:"#00172a",shade30:"#002c4e",shade20:"#003b6a",shade10:"#00467e",primary:"#004e8c",tint10:"#125e9a",tint20:"#286fa8",tint30:"#4a89ba",tint40:"#9abfdc",tint50:"#c7dced",tint60:"#f0f6fa"};var fd={shade50:"#0d1126",shade40:"#182047",shade30:"#2c3c85",shade20:"#3c51b4",shade10:"#4760d5",primary:"#4f6bed",tint10:"#637cef",tint20:"#778df1",tint30:"#93a4f4",tint40:"#c8d1fa",tint50:"#e1e6fc",tint60:"#f7f9fe"},md={shade50:"#00061d",shade40:"#000c36",shade30:"#001665",shade20:"#001e89",shade10:"#0023a2",primary:"#0027b4",tint10:"#173bbd",tint20:"#3050c6",tint30:"#546fd2",tint40:"#a3b2e8",tint50:"#ccd5f3",tint60:"#f2f4fc"},bd={shade50:"#120f25",shade40:"#221d46",shade30:"#3f3682",shade20:"#5649b0",shade10:"#6656d1",primary:"#7160e8",tint10:"#8172eb",tint20:"#9184ee",tint30:"#a79cf1",tint40:"#d2ccf8",tint50:"#e7e4fb",tint60:"#f9f8fe"},vd={shade50:"#0f0717",shade40:"#1c0e2b",shade30:"#341a51",shade20:"#46236e",shade10:"#532982",primary:"#5c2e91",tint10:"#6b3f9e",tint20:"#7c52ab",tint30:"#9470bd",tint40:"#c6b1de",tint50:"#e0d3ed",tint60:"#f7f4fb"};var kd={shade50:"#160418",shade40:"#29072e",shade30:"#4c0d55",shade20:"#671174",shade10:"#7a1589",primary:"#881798",tint10:"#952aa4",tint20:"#a33fb1",tint30:"#b55fc1",tint40:"#d9a7e0",tint50:"#eaceef",tint60:"#faf2fb"},yd={shade50:"#1f091d",shade40:"#3a1136",shade30:"#6d2064",shade20:"#932b88",shade10:"#af33a1",primary:"#c239b3",tint10:"#c94cbc",tint20:"#d161c4",tint30:"#da7ed0",tint40:"#edbbe7",tint50:"#f5daf2",tint60:"#fdf5fc"},xd={shade50:"#1c0b1f",shade40:"#35153a",shade30:"#63276d",shade20:"#863593",shade10:"#9f3faf",primary:"#b146c2",tint10:"#ba58c9",tint20:"#c36bd1",tint30:"#cf87da",tint40:"#e6bfed",tint50:"#f2dcf5",tint60:"#fcf6fd"},Sd={shade50:"#24091b",shade40:"#441232",shade30:"#80215d",shade20:"#ad2d7e",shade10:"#cd3595",primary:"#e43ba6",tint10:"#e750b0",tint20:"#ea66ba",tint30:"#ef85c8",tint40:"#f7c0e3",tint50:"#fbddf0",tint60:"#fef6fb"};var Bd={shade50:"#1f0013",shade40:"#390024",shade30:"#6b0043",shade20:"#91005a",shade10:"#ac006b",primary:"#bf0077",tint10:"#c71885",tint20:"#ce3293",tint30:"#d957a8",tint40:"#eca5d1",tint50:"#f5cee6",tint60:"#fcf2f9"},Nd={shade50:"#13000c",shade40:"#240017",shade30:"#43002b",shade20:"#5a003b",shade10:"#6b0045",primary:"#77004d",tint10:"#87105d",tint20:"#98246f",tint30:"#ad4589",tint40:"#d696c0",tint50:"#e9c4dc",tint60:"#faf0f6"},$d={shade50:"#141313",shade40:"#252323",shade30:"#444241",shade20:"#5d5958",shade10:"#6e6968",primary:"#7a7574",tint10:"#8a8584",tint20:"#9a9594",tint30:"#afabaa",tint40:"#d7d4d4",tint50:"#eae8e8",tint60:"#faf9f9"},Pd={shade50:"#0f0e0e",shade40:"#1c1b1a",shade30:"#343231",shade20:"#474443",shade10:"#54514f",primary:"#5d5a58",tint10:"#706d6b",tint20:"#84817e",tint30:"#9e9b99",tint40:"#cecccb",tint50:"#e5e4e3",tint60:"#f8f8f8"};var wd={shade50:"#111314",shade40:"#1f2426",shade30:"#3b4447",shade20:"#505c60",shade10:"#5f6d71",primary:"#69797e",tint10:"#79898d",tint20:"#89989d",tint30:"#a0adb2",tint40:"#cdd6d8",tint50:"#e4e9ea",tint60:"#f8f9fa"},Fd={shade50:"#090a0b",shade40:"#111315",shade30:"#202427",shade20:"#2b3135",shade10:"#333a3f",primary:"#394146",tint10:"#4d565c",tint20:"#626c72",tint30:"#808a90",tint40:"#bcc3c7",tint50:"#dbdfe1",tint60:"#f6f7f8"};var w={red:Yc,green:ra,darkOrange:Jc,yellow:od,berry:yd,lightGreen:ld,marigold:td},et={darkRed:Kc,cranberry:oa,pumpkin:Qc,peach:ed,gold:rd,brass:nd,brown:ad,forest:id,seafoam:sd,darkGreen:cd,lightTeal:dd,teal:ud,steel:pd,blue:hd,royalBlue:gd,cornflower:fd,navy:md,lavender:bd,purple:vd,grape:kd,lilac:xd,pink:Sd,magenta:Bd,plum:Nd,beige:$d,mink:Pd,platinum:wd,anchor:Fd},$={cranberry:oa,green:ra,orange:Zc};var on=["red","green","darkOrange","yellow","berry","lightGreen","marigold"],rn=["darkRed","cranberry","pumpkin","peach","gold","brass","brown","forest","seafoam","darkGreen","lightTeal","teal","steel","blue","royalBlue","cornflower","navy","lavender","purple","grape","lilac","pink","magenta","plum","beige","mink","platinum","anchor"];var le={success:"green",warning:"orange",danger:"cranberry"};var qo=on.reduce((t,e)=>{let o=e.slice(0,1).toUpperCase()+e.slice(1),r={[`colorPalette${o}Background1`]:w[e].tint60,[`colorPalette${o}Background2`]:w[e].tint40,[`colorPalette${o}Background3`]:w[e].primary,[`colorPalette${o}Foreground1`]:w[e].shade10,[`colorPalette${o}Foreground2`]:w[e].shade30,[`colorPalette${o}Foreground3`]:w[e].primary,[`colorPalette${o}BorderActive`]:w[e].primary,[`colorPalette${o}Border1`]:w[e].tint40,[`colorPalette${o}Border2`]:w[e].primary};return Object.assign(t,r)},{});qo.colorPaletteYellowForeground1=w.yellow.shade30;qo.colorPaletteRedForegroundInverted=w.red.tint20;qo.colorPaletteGreenForegroundInverted=w.green.tint20;qo.colorPaletteYellowForegroundInverted=w.yellow.tint40;var cp=rn.reduce((t,e)=>{let o=e.slice(0,1).toUpperCase()+e.slice(1),r={[`colorPalette${o}Background2`]:et[e].tint40,[`colorPalette${o}Foreground2`]:et[e].shade30,[`colorPalette${o}BorderActive`]:et[e].primary};return Object.assign(t,r)},{}),Td={...qo,...cp},Mt=Object.entries(le).reduce((t,[e,o])=>{let r=e.slice(0,1).toUpperCase()+e.slice(1),n={[`colorStatus${r}Background1`]:$[o].tint60,[`colorStatus${r}Background2`]:$[o].tint40,[`colorStatus${r}Background3`]:$[o].primary,[`colorStatus${r}Foreground1`]:$[o].shade10,[`colorStatus${r}Foreground2`]:$[o].shade30,[`colorStatus${r}Foreground3`]:$[o].primary,[`colorStatus${r}ForegroundInverted`]:$[o].tint30,[`colorStatus${r}BorderActive`]:$[o].primary,[`colorStatus${r}Border1`]:$[o].tint40,[`colorStatus${r}Border2`]:$[o].primary};return Object.assign(t,n)},{});Mt.colorStatusDangerBackground3Hover=$[le.danger].shade10;Mt.colorStatusDangerBackground3Pressed=$[le.danger].shade20;Mt.colorStatusWarningForeground1=$[le.warning].shade20;Mt.colorStatusWarningForeground3=$[le.warning].shade20;Mt.colorStatusWarningBorder2=$[le.warning].shade20;var Ad=t=>({colorNeutralForeground1:c[14],colorNeutralForeground1Hover:c[14],colorNeutralForeground1Pressed:c[14],colorNeutralForeground1Selected:c[14],colorNeutralForeground2:c[26],colorNeutralForeground2Hover:c[14],colorNeutralForeground2Pressed:c[14],colorNeutralForeground2Selected:c[14],colorNeutralForeground2BrandHover:t[80],colorNeutralForeground2BrandPressed:t[70],colorNeutralForeground2BrandSelected:t[80],colorNeutralForeground3:c[38],colorNeutralForeground3Hover:c[26],colorNeutralForeground3Pressed:c[26],colorNeutralForeground3Selected:c[26],colorNeutralForeground3BrandHover:t[80],colorNeutralForeground3BrandPressed:t[70],colorNeutralForeground3BrandSelected:t[80],colorNeutralForeground4:c[44],colorNeutralForeground5:c[38],colorNeutralForeground5Hover:c[14],colorNeutralForeground5Pressed:c[14],colorNeutralForeground5Selected:c[14],colorNeutralForegroundDisabled:c[74],colorNeutralForegroundInvertedDisabled:J[40],colorBrandForegroundLink:t[70],colorBrandForegroundLinkHover:t[60],colorBrandForegroundLinkPressed:t[40],colorBrandForegroundLinkSelected:t[70],colorNeutralForeground2Link:c[26],colorNeutralForeground2LinkHover:c[14],colorNeutralForeground2LinkPressed:c[14],colorNeutralForeground2LinkSelected:c[14],colorCompoundBrandForeground1:t[80],colorCompoundBrandForeground1Hover:t[70],colorCompoundBrandForeground1Pressed:t[60],colorBrandForeground1:t[80],colorBrandForeground2:t[70],colorBrandForeground2Hover:t[60],colorBrandForeground2Pressed:t[30],colorNeutralForeground1Static:c[14],colorNeutralForegroundStaticInverted:b,colorNeutralForegroundInverted:b,colorNeutralForegroundInvertedHover:b,colorNeutralForegroundInvertedPressed:b,colorNeutralForegroundInvertedSelected:b,colorNeutralForegroundInverted2:b,colorNeutralForegroundOnBrand:b,colorNeutralForegroundInvertedLink:b,colorNeutralForegroundInvertedLinkHover:b,colorNeutralForegroundInvertedLinkPressed:b,colorNeutralForegroundInvertedLinkSelected:b,colorBrandForegroundInverted:t[100],colorBrandForegroundInvertedHover:t[110],colorBrandForegroundInvertedPressed:t[100],colorBrandForegroundOnLight:t[80],colorBrandForegroundOnLightHover:t[70],colorBrandForegroundOnLightPressed:t[50],colorBrandForegroundOnLightSelected:t[60],colorNeutralBackground1:b,colorNeutralBackground1Hover:c[96],colorNeutralBackground1Pressed:c[88],colorNeutralBackground1Selected:c[92],colorNeutralBackground2:c[98],colorNeutralBackground2Hover:c[94],colorNeutralBackground2Pressed:c[86],colorNeutralBackground2Selected:c[90],colorNeutralBackground3:c[96],colorNeutralBackground3Hover:c[92],colorNeutralBackground3Pressed:c[84],colorNeutralBackground3Selected:c[88],colorNeutralBackground4:c[94],colorNeutralBackground4Hover:c[98],colorNeutralBackground4Pressed:c[96],colorNeutralBackground4Selected:b,colorNeutralBackground5:c[92],colorNeutralBackground5Hover:c[96],colorNeutralBackground5Pressed:c[94],colorNeutralBackground5Selected:c[98],colorNeutralBackground6:c[90],colorNeutralBackground7:"#00000000",colorNeutralBackground7Hover:c[92],colorNeutralBackground7Pressed:c[84],colorNeutralBackground7Selected:"#00000000",colorNeutralBackground8:c[99],colorNeutralBackgroundInverted:c[16],colorNeutralBackgroundInvertedHover:c[24],colorNeutralBackgroundInvertedPressed:c[12],colorNeutralBackgroundInvertedSelected:c[22],colorNeutralBackgroundStatic:c[20],colorNeutralBackgroundAlpha:J[50],colorNeutralBackgroundAlpha2:J[80],colorSubtleBackground:"transparent",colorSubtleBackgroundHover:c[96],colorSubtleBackgroundPressed:c[88],colorSubtleBackgroundSelected:c[92],colorSubtleBackgroundLightAlphaHover:J[70],colorSubtleBackgroundLightAlphaPressed:J[50],colorSubtleBackgroundLightAlphaSelected:"transparent",colorSubtleBackgroundInverted:"transparent",colorSubtleBackgroundInvertedHover:we[10],colorSubtleBackgroundInvertedPressed:we[30],colorSubtleBackgroundInvertedSelected:we[20],colorTransparentBackground:"transparent",colorTransparentBackgroundHover:"transparent",colorTransparentBackgroundPressed:"transparent",colorTransparentBackgroundSelected:"transparent",colorNeutralBackgroundDisabled:c[94],colorNeutralBackgroundDisabled2:b,colorNeutralBackgroundInvertedDisabled:J[10],colorNeutralStencil1:c[90],colorNeutralStencil2:c[98],colorNeutralStencil1Alpha:we[10],colorNeutralStencil2Alpha:we[5],colorBackgroundOverlay:we[40],colorScrollbarOverlay:we[50],colorBrandBackground:t[80],colorBrandBackgroundHover:t[70],colorBrandBackgroundPressed:t[40],colorBrandBackgroundSelected:t[60],colorCompoundBrandBackground:t[80],colorCompoundBrandBackgroundHover:t[70],colorCompoundBrandBackgroundPressed:t[60],colorBrandBackgroundStatic:t[80],colorBrandBackground2:t[160],colorBrandBackground2Hover:t[150],colorBrandBackground2Pressed:t[130],colorBrandBackground3Static:t[60],colorBrandBackground4Static:t[40],colorBrandBackgroundInverted:b,colorBrandBackgroundInvertedHover:t[160],colorBrandBackgroundInvertedPressed:t[140],colorBrandBackgroundInvertedSelected:t[150],colorNeutralCardBackground:c[98],colorNeutralCardBackgroundHover:b,colorNeutralCardBackgroundPressed:c[96],colorNeutralCardBackgroundSelected:c[92],colorNeutralCardBackgroundDisabled:c[94],colorNeutralStrokeAccessible:c[38],colorNeutralStrokeAccessibleHover:c[34],colorNeutralStrokeAccessiblePressed:c[30],colorNeutralStrokeAccessibleSelected:t[80],colorNeutralStroke1:c[82],colorNeutralStroke1Hover:c[78],colorNeutralStroke1Pressed:c[70],colorNeutralStroke1Selected:c[74],colorNeutralStroke2:c[88],colorNeutralStroke3:c[94],colorNeutralStroke4:c[92],colorNeutralStroke4Hover:c[88],colorNeutralStroke4Pressed:c[84],colorNeutralStroke4Selected:c[92],colorNeutralStrokeSubtle:c[88],colorNeutralStrokeOnBrand:b,colorNeutralStrokeOnBrand2:b,colorNeutralStrokeOnBrand2Hover:b,colorNeutralStrokeOnBrand2Pressed:b,colorNeutralStrokeOnBrand2Selected:b,colorBrandStroke1:t[80],colorBrandStroke2:t[140],colorBrandStroke2Hover:t[120],colorBrandStroke2Pressed:t[80],colorBrandStroke2Contrast:t[140],colorCompoundBrandStroke:t[80],colorCompoundBrandStrokeHover:t[70],colorCompoundBrandStrokePressed:t[60],colorNeutralStrokeDisabled:c[88],colorNeutralStrokeDisabled2:c[92],colorNeutralStrokeInvertedDisabled:J[40],colorTransparentStroke:"transparent",colorTransparentStrokeInteractive:"transparent",colorTransparentStrokeDisabled:"transparent",colorNeutralStrokeAlpha:we[5],colorNeutralStrokeAlpha2:J[20],colorStrokeFocus1:b,colorStrokeFocus2:yo,colorNeutralShadowAmbient:"rgba(0,0,0,0.12)",colorNeutralShadowKey:"rgba(0,0,0,0.14)",colorNeutralShadowAmbientLighter:"rgba(0,0,0,0.06)",colorNeutralShadowKeyLighter:"rgba(0,0,0,0.07)",colorNeutralShadowAmbientDarker:"rgba(0,0,0,0.20)",colorNeutralShadowKeyDarker:"rgba(0,0,0,0.24)",colorBrandShadowAmbient:"rgba(0,0,0,0.30)",colorBrandShadowKey:"rgba(0,0,0,0.25)"});var Uo={borderRadiusNone:"0",borderRadiusSmall:"2px",borderRadiusMedium:"4px",borderRadiusLarge:"6px",borderRadiusXLarge:"8px",borderRadius2XLarge:"12px",borderRadius3XLarge:"16px",borderRadius4XLarge:"24px",borderRadius5XLarge:"32px",borderRadius6XLarge:"40px",borderRadiusCircular:"10000px"};var nn={curveAccelerateMax:"cubic-bezier(0.9,0.1,1,0.2)",curveAccelerateMid:"cubic-bezier(1,0,1,1)",curveAccelerateMin:"cubic-bezier(0.8,0,0.78,1)",curveDecelerateMax:"cubic-bezier(0.1,0.9,0.2,1)",curveDecelerateMid:"cubic-bezier(0,0,0,1)",curveDecelerateMin:"cubic-bezier(0.33,0,0.1,1)",curveEasyEaseMax:"cubic-bezier(0.8,0,0.2,1)",curveEasyEase:"cubic-bezier(0.33,0,0.67,1)",curveLinear:"cubic-bezier(0,0,1,1)"};var an={durationUltraFast:"50ms",durationFaster:"100ms",durationFast:"150ms",durationNormal:"200ms",durationGentle:"250ms",durationSlow:"300ms",durationSlower:"400ms",durationUltraSlow:"500ms"};var Ko={fontSizeBase100:"10px",fontSizeBase200:"12px",fontSizeBase300:"14px",fontSizeBase400:"16px",fontSizeBase500:"20px",fontSizeBase600:"24px",fontSizeHero700:"28px",fontSizeHero800:"32px",fontSizeHero900:"40px",fontSizeHero1000:"68px"},Yo={lineHeightBase100:"14px",lineHeightBase200:"16px",lineHeightBase300:"20px",lineHeightBase400:"22px",lineHeightBase500:"28px",lineHeightBase600:"32px",lineHeightHero700:"36px",lineHeightHero800:"40px",lineHeightHero900:"52px",lineHeightHero1000:"92px"},Jo={fontWeightRegular:400,fontWeightMedium:500,fontWeightSemibold:600,fontWeightBold:700},Qo={fontFamilyBase:"'Segoe UI', 'Segoe UI Web (West European)', -apple-system, BlinkMacSystemFont, Roboto, 'Helvetica Neue', sans-serif",fontFamilyMonospace:"Consolas, 'Courier New', Courier, monospace",fontFamilyNumeric:"Bahnschrift, 'Segoe UI', 'Segoe UI Web (West European)', -apple-system, BlinkMacSystemFont, Roboto, 'Helvetica Neue', sans-serif"};var V={none:"0",xxs:"2px",xs:"4px",sNudge:"6px",s:"8px",mNudge:"10px",m:"12px",l:"16px",xl:"20px",xxl:"24px",xxxl:"32px"},sn={spacingHorizontalNone:V.none,spacingHorizontalXXS:V.xxs,spacingHorizontalXS:V.xs,spacingHorizontalSNudge:V.sNudge,spacingHorizontalS:V.s,spacingHorizontalMNudge:V.mNudge,spacingHorizontalM:V.m,spacingHorizontalL:V.l,spacingHorizontalXL:V.xl,spacingHorizontalXXL:V.xxl,spacingHorizontalXXXL:V.xxxl},ln={spacingVerticalNone:V.none,spacingVerticalXXS:V.xxs,spacingVerticalXS:V.xs,spacingVerticalSNudge:V.sNudge,spacingVerticalS:V.s,spacingVerticalMNudge:V.mNudge,spacingVerticalM:V.m,spacingVerticalL:V.l,spacingVerticalXL:V.xl,spacingVerticalXXL:V.xxl,spacingVerticalXXXL:V.xxxl};var Zo={strokeWidthThin:"1px",strokeWidthThick:"2px",strokeWidthThicker:"3px",strokeWidthThickest:"4px"};function xo(t,e,o=""){return{[`shadow2${o}`]:`0 0 2px ${t}, 0 1px 2px ${e}`,[`shadow4${o}`]:`0 0 2px ${t}, 0 2px 4px ${e}`,[`shadow8${o}`]:`0 0 2px ${t}, 0 4px 8px ${e}`,[`shadow16${o}`]:`0 0 2px ${t}, 0 8px 16px ${e}`,[`shadow28${o}`]:`0 0 8px ${t}, 0 14px 28px ${e}`,[`shadow64${o}`]:`0 0 8px ${t}, 0 32px 64px ${e}`}}var Cd=t=>{let e=Ad(t);return{...Uo,...Ko,...Yo,...Qo,...Jo,...Zo,...sn,...ln,...an,...nn,...e,...Td,...Mt,...xo(e.colorNeutralShadowAmbient,e.colorNeutralShadowKey),...xo(e.colorBrandShadowAmbient,e.colorBrandShadowKey,"Brand")}};var cn={10:"#061724",20:"#082338",30:"#0a2e4a",40:"#0c3b5e",50:"#0e4775",60:"#0f548c",70:"#115ea3",80:"#0f6cbd",90:"#2886de",100:"#479ef5",110:"#62abf5",120:"#77b7f7",130:"#96c6fa",140:"#b4d6fa",150:"#cfe4fa",160:"#ebf3fc"};var tt=on.reduce((t,e)=>{let o=e.slice(0,1).toUpperCase()+e.slice(1),r={[`colorPalette${o}Background1`]:w[e].shade40,[`colorPalette${o}Background2`]:w[e].shade30,[`colorPalette${o}Background3`]:w[e].primary,[`colorPalette${o}Foreground1`]:w[e].tint30,[`colorPalette${o}Foreground2`]:w[e].tint40,[`colorPalette${o}Foreground3`]:w[e].tint20,[`colorPalette${o}BorderActive`]:w[e].tint30,[`colorPalette${o}Border1`]:w[e].primary,[`colorPalette${o}Border2`]:w[e].tint20};return Object.assign(t,r)},{});tt.colorPaletteRedForeground3=w.red.tint30;tt.colorPaletteRedBorder2=w.red.tint30;tt.colorPaletteGreenForeground3=w.green.tint40;tt.colorPaletteGreenBorder2=w.green.tint40;tt.colorPaletteDarkOrangeForeground3=w.darkOrange.tint30;tt.colorPaletteDarkOrangeBorder2=w.darkOrange.tint30;tt.colorPaletteRedForegroundInverted=w.red.primary;tt.colorPaletteGreenForegroundInverted=w.green.primary;tt.colorPaletteYellowForegroundInverted=w.yellow.shade30;var na=rn.reduce((t,e)=>{let o=e.slice(0,1).toUpperCase()+e.slice(1),r={[`colorPalette${o}Background2`]:et[e].shade30,[`colorPalette${o}Foreground2`]:et[e].tint40,[`colorPalette${o}BorderActive`]:et[e].tint30};return Object.assign(t,r)},{});na.colorPaletteDarkRedBackground2=et.darkRed.shade20;na.colorPalettePlumBackground2=et.plum.shade20;var Id={...tt,...na},mt=Object.entries(le).reduce((t,[e,o])=>{let r=e.slice(0,1).toUpperCase()+e.slice(1),n={[`colorStatus${r}Background1`]:$[o].shade40,[`colorStatus${r}Background2`]:$[o].shade30,[`colorStatus${r}Background3`]:$[o].primary,[`colorStatus${r}Foreground1`]:$[o].tint30,[`colorStatus${r}Foreground2`]:$[o].tint40,[`colorStatus${r}Foreground3`]:$[o].tint20,[`colorStatus${r}BorderActive`]:$[o].tint30,[`colorStatus${r}ForegroundInverted`]:$[o].shade10,[`colorStatus${r}Border1`]:$[o].primary,[`colorStatus${r}Border2`]:$[o].tint20};return Object.assign(t,n)},{});mt.colorStatusDangerBackground3Hover=$[le.danger].shade10;mt.colorStatusDangerBackground3Pressed=$[le.danger].shade20;mt.colorStatusDangerForeground3=$[le.danger].tint40;mt.colorStatusDangerBorder2=$[le.danger].tint30;mt.colorStatusSuccessForeground3=$[le.success].tint40;mt.colorStatusSuccessBorder2=$[le.success].tint40;mt.colorStatusWarningForegroundInverted=$[le.warning].shade20;var er=Cd(cn);var Hd=t=>({colorNeutralForeground1:b,colorNeutralForeground1Hover:b,colorNeutralForeground1Pressed:b,colorNeutralForeground1Selected:b,colorNeutralForeground2:c[84],colorNeutralForeground2Hover:b,colorNeutralForeground2Pressed:b,colorNeutralForeground2Selected:b,colorNeutralForeground2BrandHover:t[100],colorNeutralForeground2BrandPressed:t[90],colorNeutralForeground2BrandSelected:t[100],colorNeutralForeground3:c[68],colorNeutralForeground3Hover:c[84],colorNeutralForeground3Pressed:c[84],colorNeutralForeground3Selected:c[84],colorNeutralForeground3BrandHover:t[100],colorNeutralForeground3BrandPressed:t[90],colorNeutralForeground3BrandSelected:t[100],colorNeutralForeground4:c[60],colorNeutralForeground5:c[68],colorNeutralForeground5Hover:b,colorNeutralForeground5Pressed:b,colorNeutralForeground5Selected:b,colorNeutralForegroundDisabled:c[36],colorNeutralForegroundInvertedDisabled:J[40],colorBrandForegroundLink:t[100],colorBrandForegroundLinkHover:t[110],colorBrandForegroundLinkPressed:t[90],colorBrandForegroundLinkSelected:t[100],colorNeutralForeground2Link:c[84],colorNeutralForeground2LinkHover:b,colorNeutralForeground2LinkPressed:b,colorNeutralForeground2LinkSelected:b,colorCompoundBrandForeground1:t[100],colorCompoundBrandForeground1Hover:t[110],colorCompoundBrandForeground1Pressed:t[90],colorBrandForeground1:t[100],colorBrandForeground2:t[110],colorBrandForeground2Hover:t[130],colorBrandForeground2Pressed:t[160],colorNeutralForeground1Static:c[14],colorNeutralForegroundStaticInverted:b,colorNeutralForegroundInverted:c[14],colorNeutralForegroundInvertedHover:c[14],colorNeutralForegroundInvertedPressed:c[14],colorNeutralForegroundInvertedSelected:c[14],colorNeutralForegroundInverted2:c[14],colorNeutralForegroundOnBrand:b,colorNeutralForegroundInvertedLink:b,colorNeutralForegroundInvertedLinkHover:b,colorNeutralForegroundInvertedLinkPressed:b,colorNeutralForegroundInvertedLinkSelected:b,colorBrandForegroundInverted:t[80],colorBrandForegroundInvertedHover:t[70],colorBrandForegroundInvertedPressed:t[60],colorBrandForegroundOnLight:t[80],colorBrandForegroundOnLightHover:t[70],colorBrandForegroundOnLightPressed:t[50],colorBrandForegroundOnLightSelected:t[60],colorNeutralBackground1:c[16],colorNeutralBackground1Hover:c[24],colorNeutralBackground1Pressed:c[12],colorNeutralBackground1Selected:c[22],colorNeutralBackground2:c[12],colorNeutralBackground2Hover:c[20],colorNeutralBackground2Pressed:c[8],colorNeutralBackground2Selected:c[18],colorNeutralBackground3:c[8],colorNeutralBackground3Hover:c[16],colorNeutralBackground3Pressed:c[4],colorNeutralBackground3Selected:c[14],colorNeutralBackground4:c[4],colorNeutralBackground4Hover:c[12],colorNeutralBackground4Pressed:yo,colorNeutralBackground4Selected:c[10],colorNeutralBackground5:yo,colorNeutralBackground5Hover:c[8],colorNeutralBackground5Pressed:c[2],colorNeutralBackground5Selected:c[6],colorNeutralBackground6:c[20],colorNeutralBackground7:"#00000000",colorNeutralBackground7Hover:c[10],colorNeutralBackground7Pressed:c[4],colorNeutralBackground7Selected:"#00000000",colorNeutralBackground8:c[16],colorNeutralBackgroundInverted:b,colorNeutralBackgroundInvertedHover:c[96],colorNeutralBackgroundInvertedPressed:c[88],colorNeutralBackgroundInvertedSelected:c[92],colorNeutralBackgroundStatic:c[24],colorNeutralBackgroundAlpha:qc[50],colorNeutralBackgroundAlpha2:Uc[70],colorSubtleBackground:"transparent",colorSubtleBackgroundHover:c[22],colorSubtleBackgroundPressed:c[18],colorSubtleBackgroundSelected:c[20],colorSubtleBackgroundLightAlphaHover:ta[80],colorSubtleBackgroundLightAlphaPressed:ta[50],colorSubtleBackgroundLightAlphaSelected:"transparent",colorSubtleBackgroundInverted:"transparent",colorSubtleBackgroundInvertedHover:we[10],colorSubtleBackgroundInvertedPressed:we[30],colorSubtleBackgroundInvertedSelected:we[20],colorTransparentBackground:"transparent",colorTransparentBackgroundHover:"transparent",colorTransparentBackgroundPressed:"transparent",colorTransparentBackgroundSelected:"transparent",colorNeutralBackgroundDisabled:c[8],colorNeutralBackgroundDisabled2:c[16],colorNeutralBackgroundInvertedDisabled:J[10],colorNeutralStencil1:c[34],colorNeutralStencil2:c[20],colorNeutralStencil1Alpha:J[10],colorNeutralStencil2Alpha:J[5],colorBackgroundOverlay:we[50],colorScrollbarOverlay:J[60],colorBrandBackground:t[70],colorBrandBackgroundHover:t[80],colorBrandBackgroundPressed:t[40],colorBrandBackgroundSelected:t[60],colorCompoundBrandBackground:t[100],colorCompoundBrandBackgroundHover:t[110],colorCompoundBrandBackgroundPressed:t[90],colorBrandBackgroundStatic:t[80],colorBrandBackground2:t[20],colorBrandBackground2Hover:t[40],colorBrandBackground2Pressed:t[10],colorBrandBackground3Static:t[60],colorBrandBackground4Static:t[40],colorBrandBackgroundInverted:b,colorBrandBackgroundInvertedHover:t[160],colorBrandBackgroundInvertedPressed:t[140],colorBrandBackgroundInvertedSelected:t[150],colorNeutralCardBackground:c[20],colorNeutralCardBackgroundHover:c[24],colorNeutralCardBackgroundPressed:c[18],colorNeutralCardBackgroundSelected:c[22],colorNeutralCardBackgroundDisabled:c[8],colorNeutralStrokeAccessible:c[68],colorNeutralStrokeAccessibleHover:c[74],colorNeutralStrokeAccessiblePressed:c[70],colorNeutralStrokeAccessibleSelected:t[100],colorNeutralStroke1:c[40],colorNeutralStroke1Hover:c[46],colorNeutralStroke1Pressed:c[42],colorNeutralStroke1Selected:c[44],colorNeutralStroke2:c[32],colorNeutralStroke3:c[24],colorNeutralStroke4:c[24],colorNeutralStroke4Hover:c[18],colorNeutralStroke4Pressed:c[14],colorNeutralStroke4Selected:c[24],colorNeutralStrokeSubtle:c[4],colorNeutralStrokeOnBrand:c[16],colorNeutralStrokeOnBrand2:b,colorNeutralStrokeOnBrand2Hover:b,colorNeutralStrokeOnBrand2Pressed:b,colorNeutralStrokeOnBrand2Selected:b,colorBrandStroke1:t[100],colorBrandStroke2:t[50],colorBrandStroke2Hover:t[50],colorBrandStroke2Pressed:t[30],colorBrandStroke2Contrast:t[50],colorCompoundBrandStroke:t[100],colorCompoundBrandStrokeHover:t[110],colorCompoundBrandStrokePressed:t[90],colorNeutralStrokeDisabled:c[26],colorNeutralStrokeDisabled2:c[24],colorNeutralStrokeInvertedDisabled:J[40],colorTransparentStroke:"transparent",colorTransparentStrokeInteractive:"transparent",colorTransparentStrokeDisabled:"transparent",colorNeutralStrokeAlpha:J[10],colorNeutralStrokeAlpha2:J[20],colorStrokeFocus1:yo,colorStrokeFocus2:b,colorNeutralShadowAmbient:"rgba(0,0,0,0.24)",colorNeutralShadowKey:"rgba(0,0,0,0.28)",colorNeutralShadowAmbientLighter:"rgba(0,0,0,0.12)",colorNeutralShadowKeyLighter:"rgba(0,0,0,0.14)",colorNeutralShadowAmbientDarker:"rgba(0,0,0,0.40)",colorNeutralShadowKeyDarker:"rgba(0,0,0,0.48)",colorBrandShadowAmbient:"rgba(0,0,0,0.30)",colorBrandShadowKey:"rgba(0,0,0,0.25)"});var zd=t=>{let e=Hd(t);return{...Uo,...Ko,...Yo,...Qo,...Jo,...Zo,...sn,...ln,...an,...nn,...e,...Id,...mt,...xo(e.colorNeutralShadowAmbient,e.colorNeutralShadowKey),...xo(e.colorBrandShadowAmbient,e.colorBrandShadowKey,"Brand")}};var tr=zd(cn);dt.define(Js);pt.define(Vl);ct.define(Us);Dt.define(bl);Ne.define(Sl);qr.define(Pl);Lt.define(Al);_o.define(Dl);Nt.define(Rl);co.define(Ls);Et.define(Xl);mo.define(ec);Jr.define(cc);Yr.define(ic);bo.define(hc);Zr.define(bc);vo.define(xc);ko.define(Pc);Pe.define(_c);Rt.define(Ec);Ot.define(Ac);$t.define(Xc);var aa=window.matchMedia("(prefers-color-scheme: dark)"),Dd=()=>{tn(aa.matches?tr:er),document.documentElement.dataset.theme=aa.matches?"dark":"light"};Dd();aa.addEventListener("change",Dd);
