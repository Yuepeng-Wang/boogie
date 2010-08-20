//-----------------------------------------------------------------------------
//
// Copyright (C) Microsoft Corporation.  All Rights Reserved.
//
//-----------------------------------------------------------------------------
//---------------------------------------------------------------------------------------------
// BoogiePL - Absy.cs
//---------------------------------------------------------------------------------------------

namespace Microsoft.Boogie 
{
  using System;
  using System.Collections;
  using System.Diagnostics;
  using System.Collections.Generic;
  using Microsoft.Boogie.AbstractInterpretation;
  using AI = Microsoft.AbstractInterpretationFramework;
  using Microsoft.Contracts;

  //=====================================================================
  //---------------------------------------------------------------------
  // Types

  public abstract class Type : Absy {
    public Type(IToken! token)
      : base(token) 
    {
    }

    //-----------  Cloning  ----------------------------------
    // We implement our own clone-method, because bound type variables
    // have to be created in the right way. It is /not/ ok to just clone
    // everything recursively. Applying Clone to a type will return
    // a type in which all bound variables have been replaced with new
    // variables, whereas free variables have not changed

    public override Absy! Clone() {
      return this.Clone(new Dictionary<TypeVariable!, TypeVariable!> ());
    }

    public abstract Type! Clone(IDictionary<TypeVariable!, TypeVariable!>! varMap);

    /// <summary>
    /// Clones the type, but only syntactically.  Anything resolved in the source
    /// type is left unresolved (that is, with just the name) in the destination type.
    /// </summary>
    public abstract Type! CloneUnresolved();
    
    //-----------  Linearisation  ----------------------------------

    public void Emit(TokenTextWriter! stream) {
      this.Emit(stream, 0);
    }

    public abstract void Emit(TokenTextWriter! stream, int contextBindingStrength);

    [Pure]
    public override string! ToString() {
      System.IO.StringWriter buffer = new System.IO.StringWriter();
      using (TokenTextWriter stream = new TokenTextWriter("<buffer>", buffer, false))
      {
        this.Emit(stream);
      }
      return buffer.ToString();
    }
    
    //-----------  Equality  ----------------------------------

    [Pure][Reads(ReadsAttribute.Reads.Nothing)]
    public override bool Equals(object that) 
    {
      if (ReferenceEquals(this, that))
        return true;
      Type thatType = that as Type;
      return thatType != null && this.Equals(thatType,
                                             new TypeVariableSeq (),
                                             new TypeVariableSeq ());
    }

    [Pure]
    public abstract bool Equals(Type! that,
                                TypeVariableSeq! thisBoundVariables,
                                TypeVariableSeq! thatBoundVariables);

    // used to skip leading type annotations (subexpressions of the
    // resulting type might still contain annotations)
    internal virtual Type! Expanded { get {
      return this;
    } }

    //-----------  Unification of types  -----------

    /// <summary>
    /// Add a constraint that this==that, if possible, and return true.
    /// If not possible, return false (which may have added some partial constraints).
    /// No error is printed.
    /// </summary>
    public bool Unify(Type! that) {
      return Unify(that, new TypeVariableSeq(), new Dictionary<TypeVariable!, Type!> ());
    }

    public abstract bool Unify(Type! that,
                               TypeVariableSeq! unifiableVariables,
                               // an idempotent substitution that describes the
                               // unification result up to a certain point
                               IDictionary<TypeVariable!, Type!>! unifier);
      requires forall{TypeVariable key in unifier.Keys; unifiableVariables.Has(key)};
      requires IsIdempotent(unifier);

    [Pure]
    public static bool IsIdempotent(IDictionary<TypeVariable!, Type!>! unifier) {
      return forall{Type! t in unifier.Values;
             forall{TypeVariable! var in t.FreeVariables;
                                   !unifier.ContainsKey(var)}};
    }


#if OLD_UNIFICATION    
    // Compute a most general unification of two types. null is returned if
    // no such unifier exists. The unifier is not allowed to subtitute any
    // type variables other than the ones in "unifiableVariables"
    public IDictionary<TypeVariable!, Type!> Unify(Type! that,
                                                   TypeVariableSeq! unifiableVariables) {
      Dictionary<TypeVariable!, Type!>! result = new Dictionary<TypeVariable!, Type!> ();
      try {
        this.Unify(that, unifiableVariables,
                   new TypeVariableSeq (), new TypeVariableSeq (), result);
      } catch (UnificationFailedException) {
        return null;
      }
      return result;
    }

    // Compute an idempotent most general unifier and add the result to the argument
    // unifier. The result is true iff the unification succeeded
    public bool Unify(Type! that,
                      TypeVariableSeq! unifiableVariables,
                      // given mappings that need to be taken into account
                      // the old unifier has to be idempotent as well
                      IDictionary<TypeVariable!, Type!>! unifier)
      requires forall{TypeVariable key in unifier.Keys; unifiableVariables.Has(key)};
      requires IsIdempotent(unifier);
    {
      try {
        this.Unify(that, unifiableVariables,
                   new TypeVariableSeq (), new TypeVariableSeq (), unifier);
      } catch (UnificationFailedException) {
        return false;
      }
      return true;
    }

    public abstract void Unify(Type! that,
                               TypeVariableSeq! unifiableVariables,
                               TypeVariableSeq! thisBoundVariables,
                               TypeVariableSeq! thatBoundVariables,
                               // an idempotent substitution that describes the
                               // unification result up to a certain point
                               IDictionary<TypeVariable!, Type!>! result);
#endif

    //-----------  Substitution of free variables with types not containing bound variables  -----------------

    public abstract Type! Substitute(IDictionary<TypeVariable!, Type!>! subst);
    
    //-----------  Hashcodes  ----------------------------------

    // Hack to be able to access the hashcode of superclasses further up
    // (from the subclasses of this class)
    [Pure]
    protected int GetBaseHashCode() {
      return base.GetHashCode();
    }

    [Pure]
    public override int GetHashCode() 
    {
      return this.GetHashCode(new TypeVariableSeq ());
    }

    [Pure]
    public abstract int GetHashCode(TypeVariableSeq! boundVariables);

    //-----------  Resolution  ----------------------------------

    public override void Resolve(ResolutionContext! rc) 
    {
      System.Diagnostics.Debug.Fail("Type.Resolve should never be called." +
                                    " Use Type.ResolveType instead");
    }

    public abstract Type! ResolveType(ResolutionContext! rc);

    public override void Typecheck(TypecheckingContext! tc) 
    {
      System.Diagnostics.Debug.Fail("Type.Typecheck should never be called");
    }

    // determine the free variables in a type, in the order in which the variables occur
    public abstract TypeVariableSeq! FreeVariables { get; }

    // determine the free type proxies in a type, in the order in which they occur
    public abstract List<TypeProxy!>! FreeProxies { get; }

    protected static void AppendWithoutDups<A>(List<A>! a, List<A>! b) {
      foreach (A x in b)
        if (!a.Contains(x))
          a.Add(x);
    }

    public bool IsClosed { get {
      return FreeVariables.Length == 0;
    } }

    //-----------  Getters/Issers  ----------------------------------

    // the following methods should be used instead of simple casts or the
    // C# "is" operator, because they handle type synonym annotations and
    // type proxies correctly

    public virtual bool IsBasic { get { return false; } }
    public virtual bool IsInt { get { return false; } }
    public virtual bool IsBool { get { return false; } }

    public virtual bool IsVariable { get { return false; } }
    public virtual TypeVariable! AsVariable { get {
      assert false;  // Type.AsVariable should never be called
    } }
    public virtual bool IsCtor { get { return false; } }
    public virtual CtorType! AsCtor { get {
      assert false;  // Type.AsCtor should never be called
    } }
    public virtual bool IsMap { get { return false; } }
    public virtual MapType! AsMap { get {
      assert false;  // Type.AsMap should never be called
    } }
    public virtual int MapArity { get {
      assert false;  // Type.MapArity should never be called
    } }
    public virtual bool IsUnresolved { get { return false; } }
    public virtual UnresolvedTypeIdentifier! AsUnresolved { get {
      assert false;  // Type.AsUnresolved should never be called
    } }

    public virtual bool IsBv { get { return false; } }
    public virtual int BvBits { get {
      assert false;  // Type.BvBits should never be called
    } }

    public static readonly Type! Int = new BasicType(SimpleType.Int);
    public static readonly Type! Bool = new BasicType(SimpleType.Bool);
    private static BvType[] bvtypeCache;
    
    static public BvType! GetBvType(int sz)
      requires 0 <= sz;
    {
      if (bvtypeCache == null) {
        bvtypeCache = new BvType[128];
      }
      if (sz < bvtypeCache.Length) {
        BvType t = bvtypeCache[sz];
        if (t == null) {
          t = new BvType(sz);
          bvtypeCache[sz] = t;
        }
        return t;
      } else {
        return new BvType(sz);
      }
    }

    //------------ Match formal argument types on actual argument types
    //------------ and return the resulting substitution of type variables

#if OLD_UNIFICATION
    public static IDictionary<TypeVariable!, Type!>!
                  MatchArgumentTypes(TypeVariableSeq! typeParams,
                                     TypeSeq! formalArgs,
                                     ExprSeq! actualArgs,
                                     TypeSeq formalOuts,
                                     IdentifierExprSeq actualOuts,
                                     string! opName,
                                     TypecheckingContext! tc)
      requires formalArgs.Length == actualArgs.Length;
      requires formalOuts == null <==> actualOuts == null;
      requires formalOuts != null ==> formalOuts.Length == actualOuts.Length;
    {
      TypeVariableSeq! boundVarSeq0 = new TypeVariableSeq ();
      TypeVariableSeq! boundVarSeq1 = new TypeVariableSeq ();
      Dictionary<TypeVariable!, Type!>! subst = new Dictionary<TypeVariable!, Type!>();

      for (int i = 0; i < formalArgs.Length; ++i) {
        try {
          Type! actualType = (!)((!)actualArgs[i]).Type;
          // if the type variables to be matched occur in the actual
          // argument types, something has gone very wrong
          assert forall{TypeVariable! var in typeParams;
                        !actualType.FreeVariables.Has(var)};
          formalArgs[i].Unify(actualType,
                              typeParams,
                              boundVarSeq0, boundVarSeq1,
                              subst);
        } catch (UnificationFailedException) {
          tc.Error(actualArgs[i],
                   "invalid type for argument {0} in {1}: {2} (expected: {3})",
                   i, opName, actualArgs[i].Type,
                   // we insert the type parameters that have already been
                   // chosen to get a more precise error message
                   formalArgs[i].Substitute(subst));
          // the bound variable sequences should be empty ...
          // so that we can continue with the unification
          assert boundVarSeq0.Length == 0 && boundVarSeq1.Length == 0;
        }
      }
      
      if (formalOuts != null) {
        for (int i = 0; i < formalOuts.Length; ++i) {
          try {
            Type! actualType = (!)((!)actualOuts[i]).Type;
            // if the type variables to be matched occur in the actual
            // argument types, something has gone very wrong
            assert forall{TypeVariable! var in typeParams;
                          !actualType.FreeVariables.Has(var)};
            formalOuts[i].Unify(actualType,
                                typeParams,
                                boundVarSeq0, boundVarSeq1,
                                subst);
          } catch (UnificationFailedException) {
            tc.Error(actualOuts[i],
                     "invalid type for result {0} in {1}: {2} (expected: {3})",
                     i, opName, actualOuts[i].Type,
                     // we insert the type parameters that have already been
                     // chosen to get a more precise error message
                     formalOuts[i].Substitute(subst));
            // the bound variable sequences should be empty ...
            // so that we can continue with the unification
            assert boundVarSeq0.Length == 0 && boundVarSeq1.Length == 0;
          }
        }
      }

      // we only allow type parameters to be substituted
      assert forall{TypeVariable! var in subst.Keys; typeParams.Has(var)};

      return subst;
    }
#else
    public static IDictionary<TypeVariable!, Type!>!
                  MatchArgumentTypes(TypeVariableSeq! typeParams,
                                     TypeSeq! formalArgs,
                                     ExprSeq! actualArgs,
                                     TypeSeq formalOuts,
                                     IdentifierExprSeq actualOuts,
                                     string! opName,
                                     TypecheckingContext! tc)
      requires formalArgs.Length == actualArgs.Length;
      requires formalOuts == null <==> actualOuts == null;
      requires formalOuts != null ==> formalOuts.Length == ((!)actualOuts).Length;
      requires tc != null ==> opName != null;
      // requires "actualArgs" and "actualOuts" to have been type checked
    {
      Dictionary<TypeVariable!, Type!> subst = new Dictionary<TypeVariable!, Type!>();
      foreach (TypeVariable! tv in typeParams) {
        TypeProxy proxy = new TypeProxy(Token.NoToken, tv.Name);
        subst.Add(tv, proxy);
      }
      
      for (int i = 0; i < formalArgs.Length; i++) {
        Type formal = formalArgs[i].Substitute(subst);
        Type actual = (!)((!)actualArgs[i]).Type;
        // if the type variables to be matched occur in the actual
        // argument types, something has gone very wrong
        assert forall{TypeVariable! var in typeParams; !actual.FreeVariables.Has(var)};

        if (!formal.Unify(actual)) {
          assume tc != null;  // caller expected no errors
          assert opName != null;  // follows from precondition
          tc.Error((!)actualArgs[i],
                   "invalid type for argument {0} in {1}: {2} (expected: {3})",
                   i, opName, actual, formalArgs[i]);
        }
      }
      
      if (formalOuts != null) {
        for (int i = 0; i < formalOuts.Length; ++i) {
          Type formal = formalOuts[i].Substitute(subst);
          Type actual = (!)((!)actualOuts)[i].Type;
          // if the type variables to be matched occur in the actual
          // argument types, something has gone very wrong
          assert forall{TypeVariable! var in typeParams; !actual.FreeVariables.Has(var)};

          if (!formal.Unify(actual)) {
            assume tc != null;  // caller expected no errors
            assert opName != null;  // follows from precondition
            tc.Error(actualOuts[i],
                     "invalid type for out-parameter {0} in {1}: {2} (expected: {3})",
                     i, opName, actual, formal);
          }
        }
      }
      
      return subst;
    }
#endif

    //------------  Match formal argument types of a function or map
    //------------  on concrete types, substitute the result into the
    //------------  result type. Null is returned for type errors

    public static TypeSeq CheckArgumentTypes(TypeVariableSeq! typeParams,
                                             out List<Type!>! actualTypeParams,
                                             TypeSeq! formalIns,
                                             ExprSeq! actualIns,
                                             TypeSeq! formalOuts,
                                             IdentifierExprSeq actualOuts,
                                             IToken! typeCheckingSubject,
                                             string! opName,
                                             TypecheckingContext! tc)
      // requires "actualIns" and "actualOuts" to have been type checked
    {
      actualTypeParams = new List<Type!> ();

      if (formalIns.Length != actualIns.Length) {
        tc.Error(typeCheckingSubject, "wrong number of arguments in {0}: {1}",
                 opName, actualIns.Length);
        // if there are no type parameters, we can still return the result
        // type and hope that the type checking proceeds
        return typeParams.Length == 0 ? formalOuts : null;
      } else if (actualOuts != null && formalOuts.Length != actualOuts.Length) {
        tc.Error(typeCheckingSubject, "wrong number of result variables in {0}: {1}",
                 opName, actualOuts.Length);
        // if there are no type parameters, we can still return the result
        // type and hope that the type checking proceeds
        actualTypeParams = new List<Type!> ();
        return typeParams.Length == 0 ? formalOuts : null;
      }

      int previousErrorCount = tc.ErrorCount;
      IDictionary<TypeVariable!, Type!> subst =
        MatchArgumentTypes(typeParams, formalIns, actualIns,
                           actualOuts != null ? formalOuts : null, actualOuts, opName, tc);

      foreach (TypeVariable! var in typeParams)
        actualTypeParams.Add(subst[var]);

      TypeSeq! actualResults = new TypeSeq ();
      foreach (Type! t in formalOuts) {
        actualResults.Add(t.Substitute(subst));
      }
      TypeVariableSeq resultFreeVars = FreeVariablesIn(actualResults);
      if (previousErrorCount != tc.ErrorCount) {
        // errors occured when matching the formal arguments
        // in case we have been able to substitute all type parameters,
        // we can still return the result type and hope that the
        // type checking proceeds in a meaningful manner
        if (forall{TypeVariable! var in typeParams; !resultFreeVars.Has(var)})
          return actualResults;
        else
          // otherwise there is no point in returning the result type,
          // type checking would only get confused even further
          return null;
      }

      assert forall{TypeVariable! var in typeParams; !resultFreeVars.Has(var)};
      return actualResults;
    }

    ///////////////////////////////////////////////////////////////////////////

    // about the same as Type.CheckArgumentTypes, but without
    // detailed error reports
    public static Type! InferValueType(TypeVariableSeq! typeParams,
                                       TypeSeq! formalArgs,
                                       Type! formalResult,
                                       TypeSeq! actualArgs) {
      IDictionary<TypeVariable!, Type!>! subst =
        InferTypeParameters(typeParams, formalArgs, actualArgs);

      Type! res = formalResult.Substitute(subst);
      // all type parameters have to be substituted with concrete types
      TypeVariableSeq! resFreeVars = res.FreeVariables;
      assert forall{TypeVariable! var in typeParams; !resFreeVars.Has(var)};
      return res;
    }

#if OLD_UNIFICATION
    public static IDictionary<TypeVariable!, Type!>!
                  InferTypeParameters(TypeVariableSeq! typeParams,
                                      TypeSeq! formalArgs,
                                      TypeSeq! actualArgs)
      requires formalArgs.Length == actualArgs.Length; {
      
      TypeVariableSeq! boundVarSeq0 = new TypeVariableSeq ();
      TypeVariableSeq! boundVarSeq1 = new TypeVariableSeq ();
      Dictionary<TypeVariable!, Type!>! subst = new Dictionary<TypeVariable!, Type!>();

      for (int i = 0; i < formalArgs.Length; ++i) {
        try {
          assert forall{TypeVariable! var in typeParams;
                        !actualArgs[i].FreeVariables.Has(var)};
          formalArgs[i].Unify(actualArgs[i], typeParams,
                              boundVarSeq0, boundVarSeq1, subst);
        } catch (UnificationFailedException) {
          System.Diagnostics.Debug.Fail("Type unification failed: " +
                                        formalArgs[i] + " vs " + actualArgs[i]);
        }
      }

      // we only allow type parameters to be substituted
      assert forall{TypeVariable! var in subst.Keys; typeParams.Has(var)};
      return subst;
    }
#else
    /// <summary>
    /// like Type.CheckArgumentTypes, but assumes no errors
    /// (and only does arguments, not results; and takes actuals as TypeSeq, not ExprSeq)
    /// </summary>
    public static IDictionary<TypeVariable!, Type!>!
                  InferTypeParameters(TypeVariableSeq! typeParams,
                                      TypeSeq! formalArgs,
                                      TypeSeq! actualArgs)
      requires formalArgs.Length == actualArgs.Length;
    {
      TypeSeq proxies = new TypeSeq();
      Dictionary<TypeVariable!, Type!>! subst = new Dictionary<TypeVariable!, Type!>();
      foreach (TypeVariable! tv in typeParams) {
        TypeProxy proxy = new TypeProxy(Token.NoToken, tv.Name);
        proxies.Add(proxy);
        subst.Add(tv, proxy);
      }
      
      for (int i = 0; i < formalArgs.Length; i++) {
        Type formal = formalArgs[i].Substitute(subst);
        Type actual = actualArgs[i];
        // if the type variables to be matched occur in the actual
        // argument types, something has gone very wrong
        assert forall{TypeVariable! var in typeParams; !actual.FreeVariables.Has(var)};

        if (!formal.Unify(actual)) {
          assume false;  // caller expected no errors
        }
      }
      
      return subst;
    }
#endif
    
    //-----------  Helper methods to deal with bound type variables  ---------------

    public static void EmitOptionalTypeParams(TokenTextWriter! stream, TypeVariableSeq! typeParams) {
      if (typeParams.Length > 0) {
        stream.Write("<");
        typeParams.Emit(stream, ","); // default binding strength of 0 is ok
        stream.Write(">");
      }
    }

    // Sort the type parameters according to the order of occurrence in the argument types
    public static TypeVariableSeq! SortTypeParams(TypeVariableSeq! typeParams,
                                                  TypeSeq! argumentTypes, Type resultType)
      ensures result.Length == typeParams.Length; {
      if (typeParams.Length == 0) {
        return typeParams;
      }

      TypeVariableSeq freeVarsInUse = FreeVariablesIn(argumentTypes);
      if (resultType != null) {
        freeVarsInUse.AppendWithoutDups(resultType.FreeVariables);
      }
      // "freeVarsInUse" is already sorted, but it may contain type variables not in "typeParams".
      // So, project "freeVarsInUse" onto "typeParams":
      TypeVariableSeq! sortedTypeParams = new TypeVariableSeq ();
      foreach (TypeVariable! var in freeVarsInUse) {
        if (typeParams.Has(var)) {
          sortedTypeParams.Add(var);
        }
      }
      
      if (sortedTypeParams.Length < typeParams.Length)
        // add the type parameters not mentioned in "argumentTypes" in
        // the end of the list (this can happen for quantifiers)
        sortedTypeParams.AppendWithoutDups(typeParams);

      return sortedTypeParams;
    }

    // Check that each of the type parameters occurs in at least one argument type.
    // Return true if some type parameters appear only among "moreArgumentTypes" and
    // not in "argumentTypes".
    [Pure]
    public static bool CheckBoundVariableOccurrences(TypeVariableSeq! typeParams,
                                                     TypeSeq! argumentTypes,
                                                     TypeSeq moreArgumentTypes,
                                                     IToken! resolutionSubject,
                                                     string! subjectName,
                                                     ResolutionContext! rc) {
      TypeVariableSeq freeVarsInArgs = FreeVariablesIn(argumentTypes);
      TypeVariableSeq moFreeVarsInArgs = moreArgumentTypes == null ? null : FreeVariablesIn(moreArgumentTypes);
      bool someTypeParamsAppearOnlyAmongMo = false;
      foreach (TypeVariable! var in typeParams) {
        if (rc.LookUpTypeBinder(var.Name) == var)  // avoid to complain twice about variables that are bound multiple times
        {
          if (freeVarsInArgs.Has(var)) {
            // cool
          } else if (moFreeVarsInArgs != null && moFreeVarsInArgs.Has(var)) {
            someTypeParamsAppearOnlyAmongMo = true;
          } else {
            rc.Error(resolutionSubject,
                 "type variable must occur in {0}: {1}",
                 subjectName, var);
          }
        }
      }
      return someTypeParamsAppearOnlyAmongMo;
    }

    [Pure]
    public static TypeVariableSeq! FreeVariablesIn(TypeSeq! arguments) {
      TypeVariableSeq! res = new TypeVariableSeq ();
      foreach (Type! t in arguments)
        res.AppendWithoutDups(t.FreeVariables);
      return res;
    }
  }

  //=====================================================================

  public class BasicType : Type
  {
    public readonly SimpleType T;
    public BasicType(IToken! token, SimpleType t)
      : base(token)
    {
      T = t;
      // base(token);
    }
    public BasicType(SimpleType t)
      : base(Token.NoToken)
    {
      T = t;
      // base(Token.NoToken);
    }

    //-----------  Cloning  ----------------------------------
    // We implement our own clone-method, because bound type variables
    // have to be created in the right way. It is /not/ ok to just clone
    // everything recursively.

    public override Type! Clone(IDictionary<TypeVariable!, TypeVariable!>! varMap) {
      // BasicTypes are immutable anyway, we do not clone
      return this;
    }

    public override Type! CloneUnresolved() {
      return this;
    }

    //-----------  Linearisation  ----------------------------------

    public override void Emit(TokenTextWriter! stream, int contextBindingStrength) 
    {
      // no parentheses are necessary for basic types
      stream.SetToken(this);
      stream.Write("{0}", this);
    }

    [Pure]
    public override string! ToString()
    {
      switch (T) 
      {
        case SimpleType.Int: return "int";
        case SimpleType.Bool: return "bool";
      }
      Debug.Assert(false, "bad type " + T);
      assert false;  // make compiler happy
    }

    //-----------  Equality  ----------------------------------

    [Pure][Reads(ReadsAttribute.Reads.Nothing)]
    public override bool Equals(object that) {
      // shortcut
      Type thatType = that as Type;
      if (thatType == null)
        return false;
      BasicType thatBasicType = TypeProxy.FollowProxy(thatType.Expanded) as BasicType;
      return thatBasicType != null && this.T == thatBasicType.T;
    }

    [Pure]
    public override bool Equals(Type! that,
                                TypeVariableSeq! thisBoundVariables,
                                TypeVariableSeq! thatBoundVariables) {
      return this.Equals(that);
    }

    //-----------  Unification of types  -----------

    public override bool Unify(Type! that,
                               TypeVariableSeq! unifiableVariables,
                               // an idempotent substitution that describes the
                               // unification result up to a certain point
                               IDictionary<TypeVariable!, Type!>! unifier) {
      that = that.Expanded;
      if (that is TypeProxy || that is TypeVariable) {
        return that.Unify(this, unifiableVariables, unifier);
      } else {
        return this.Equals(that);
      }
    }

#if OLD_UNIFICATION
    public override void Unify(Type! that,
                               TypeVariableSeq! unifiableVariables,
                               TypeVariableSeq! thisBoundVariables,
                               TypeVariableSeq! thatBoundVariables,
                               IDictionary<TypeVariable!, Type!>! result) {
      that = that.Expanded;
      if (that is TypeVariable) {
        that.Unify(this, unifiableVariables, thatBoundVariables, thisBoundVariables, result);
      } else {
        if (!this.Equals(that))
          throw UNIFICATION_FAILED;
      }
    }
#endif

    //-----------  Substitution of free variables with types not containing bound variables  -----------------

    public override Type! Substitute(IDictionary<TypeVariable!, Type!>! subst) {
      return this;
    }

    //-----------  Hashcodes  ----------------------------------

    [Pure]
    public override int GetHashCode(TypeVariableSeq! boundVariables)
    {
      return this.T.GetHashCode();
    }

    //-----------  Resolution  ----------------------------------

    public override Type! ResolveType(ResolutionContext! rc) {
      // nothing to resolve
      return this;
    }

    // determine the free variables in a type, in the order in which the variables occur
    public override TypeVariableSeq! FreeVariables {
      get {
        return new TypeVariableSeq ();  // basic type are closed
      }
    }

    public override List<TypeProxy!>! FreeProxies { get {
      return new List<TypeProxy!> ();
    } }

    //-----------  Getters/Issers  ----------------------------------

    public override bool IsBasic { get { return true; } }
    public override bool IsInt { get { return this.T == SimpleType.Int; } }
    public override bool IsBool { get { return this.T == SimpleType.Bool; } }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitBasicType(this);
    }
  }
  
  //=====================================================================

  public class BvType : Type
  {
    public readonly int Bits;
    
    public BvType(IToken! token, int bits)
      : base(token)
    {
      Bits = bits;
    }
    
    public BvType(int bits)
      : base(Token.NoToken)
    {
      Bits = bits;
    }

    //-----------  Cloning  ----------------------------------
    // We implement our own clone-method, because bound type variables
    // have to be created in the right way. It is /not/ ok to just clone
    // everything recursively.

    public override Type! Clone(IDictionary<TypeVariable!, TypeVariable!>! varMap) {
      // BvTypes are immutable anyway, we do not clone
      return this;
    }

    public override Type! CloneUnresolved() {
      return this;
    }

    //-----------  Linearisation  ----------------------------------

    public override void Emit(TokenTextWriter! stream, int contextBindingStrength) 
    {
      // no parentheses are necessary for bitvector-types
      stream.SetToken(this);
      stream.Write("{0}", this);
    }

    [Pure]
    public override string! ToString()
    {
      return "bv" + Bits;
    }

    //-----------  Equality  ----------------------------------

    [Pure]
    public override bool Equals(Type! that,
                                TypeVariableSeq! thisBoundVariables,
                                TypeVariableSeq! thatBoundVariables) {
      BvType thatBvType = TypeProxy.FollowProxy(that.Expanded) as BvType;
      return thatBvType != null && this.Bits == thatBvType.Bits;
    }

    //-----------  Unification of types  -----------

    public override bool Unify(Type! that,
                               TypeVariableSeq! unifiableVariables,
                               // an idempotent substitution that describes the
                               // unification result up to a certain point
                               IDictionary<TypeVariable!, Type!>! unifier) {
      that = that.Expanded;
      if (that is TypeProxy || that is TypeVariable) {
        return that.Unify(this, unifiableVariables, unifier);
      } else {
        return this.Equals(that);
      }
    }

#if OLD_UNIFICATION
    public override void Unify(Type! that,
                               TypeVariableSeq! unifiableVariables,
                               TypeVariableSeq! thisBoundVariables,
                               TypeVariableSeq! thatBoundVariables,
                               IDictionary<TypeVariable!, Type!>! result) {
      that = that.Expanded;
      if (that is TypeVariable) {
        that.Unify(this, unifiableVariables, thatBoundVariables, thisBoundVariables, result);
      } else {
        if (!this.Equals(that))
          throw UNIFICATION_FAILED;
      }
    }
#endif

    //-----------  Substitution of free variables with types not containing bound variables  -----------------

    public override Type! Substitute(IDictionary<TypeVariable!, Type!>! subst) {
      return this;
    }

    //-----------  Hashcodes  ----------------------------------

    [Pure]
    public override int GetHashCode(TypeVariableSeq! boundVariables)
    {
      return this.Bits.GetHashCode();
    }

    //-----------  Resolution  ----------------------------------

    public override Type! ResolveType(ResolutionContext! rc) {
      // nothing to resolve
      return this;
    }

    // determine the free variables in a type, in the order in which the variables occur
    public override TypeVariableSeq! FreeVariables {
      get {
        return new TypeVariableSeq ();  // bitvector-type are closed
      }
    }

    public override List<TypeProxy!>! FreeProxies { get {
      return new List<TypeProxy!> ();
    } }

    //-----------  Getters/Issers  ----------------------------------

    public override bool IsBv { get { return true; } }
    public override int BvBits { get {
      return Bits;
    } }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitBvType(this);
    }
  }

  //=====================================================================

  // An AST node containing an identifier and a sequence of type arguments, which
  // will be turned either into a TypeVariable, into a CtorType or into a BvType
  // during the resolution phase
  public class UnresolvedTypeIdentifier : Type {
    public readonly string! Name;
    public readonly TypeSeq! Arguments;

    public UnresolvedTypeIdentifier(IToken! token, string! name) {
      this(token, name, new TypeSeq ());
    }

    public UnresolvedTypeIdentifier(IToken! token, string! name, TypeSeq! arguments)
      : base(token) 
    {
      this.Name = name;
      this.Arguments = arguments;
    }

    //-----------  Cloning  ----------------------------------
    // We implement our own clone-method, because bound type variables
    // have to be created in the right way. It is /not/ ok to just clone
    // everything recursively

    public override Type! Clone(IDictionary<TypeVariable!, TypeVariable!>! varMap) {
      TypeSeq! newArgs = new TypeSeq ();
      foreach(Type! t in Arguments)
        newArgs.Add(t.Clone(varMap));
      return new UnresolvedTypeIdentifier(tok, Name, newArgs);
    }

    public override Type! CloneUnresolved() {
      TypeSeq! newArgs = new TypeSeq ();
      foreach(Type! t in Arguments)
        newArgs.Add(t.CloneUnresolved());
      return new UnresolvedTypeIdentifier(tok, Name, newArgs);
    }

    //-----------  Equality  ----------------------------------

    [Pure]
    public override bool Equals(Type! that,
                                TypeVariableSeq! thisBoundVariables,
                                TypeVariableSeq! thatBoundVariables) {
      System.Diagnostics.Debug.Fail("UnresolvedTypeIdentifier.Equals should never be called");
      return false; // to make the compiler happy      
    }

    //-----------  Unification of types  -----------

    public override bool Unify(Type! that,
                               TypeVariableSeq! unifiableVariables,
                               IDictionary<TypeVariable!, Type!>! result) {
      assert false;  // UnresolvedTypeIdentifier.Unify should never be called
    }

#if OLD_UNIFICATION
    public override void Unify(Type! that,
                               TypeVariableSeq! unifiableVariables,
                               TypeVariableSeq! thisBoundVariables,
                               TypeVariableSeq! thatBoundVariables,
                               IDictionary<TypeVariable!, Type!>! result) {
      System.Diagnostics.Debug.Fail("UnresolvedTypeIdentifier.Unify should never be called");
    }
#endif

    //-----------  Substitution of free variables with types not containing bound variables  -----------------

    public override Type! Substitute(IDictionary<TypeVariable!, Type!>! subst) {
      assert false;  // UnresolvedTypeIdentifier.Substitute should never be called
    }

    //-----------  Hashcodes  ----------------------------------

    [Pure]
    public override int GetHashCode(TypeVariableSeq! boundVariables) {
      assert false;  // UnresolvedTypeIdentifier.GetHashCode should never be called
    }

    //-----------  Resolution  ----------------------------------

    public override Type! ResolveType(ResolutionContext! rc) {
      // first case: the type name denotes a bitvector-type
      if (Name.StartsWith("bv") && Name.Length > 2) {
        bool is_bv = true;
        for (int i = 2; i < Name.Length; ++i) {
          if (!char.IsDigit(Name[i])) {
            is_bv = false;
            break;
          }
        }
        if (is_bv) {
          if (Arguments.Length > 0) {
            rc.Error(this,
                     "bitvector types must not be applied to arguments: {0}",
                     Name);
          }
          return new BvType(tok, int.Parse(Name.Substring(2)));
        }
      }

      // second case: the identifier is resolved to a type variable
      TypeVariable var = rc.LookUpTypeBinder(Name);
      if (var != null) {
        if (Arguments.Length > 0) {
          rc.Error(this,
                   "type variables must not be applied to arguments: {0}",
                   var);
        }
        return var;
      }

      // third case: the identifier denotes a type constructor and we
      // recursively resolve the arguments
      TypeCtorDecl ctorDecl = rc.LookUpType(Name);
      if (ctorDecl != null) {
        if (Arguments.Length != ctorDecl.Arity) {
          rc.Error(this,
                   "type constructor received wrong number of arguments: {0}",
                   ctorDecl);
          return this;
        }
        return new CtorType (tok, ctorDecl, ResolveArguments(rc));
      }

      // fourth case: the identifier denotes a type synonym
      TypeSynonymDecl synDecl = rc.LookUpTypeSynonym(Name);
      if (synDecl != null) {
        if (Arguments.Length != synDecl.TypeParameters.Length) {
          rc.Error(this,
                   "type synonym received wrong number of arguments: {0}",
                   synDecl);
          return this;
        }
        TypeSeq! resolvedArgs = ResolveArguments(rc);


        return new TypeSynonymAnnotation(this.tok, synDecl, resolvedArgs);

      }

      // otherwise: this name is not declared anywhere
      rc.Error(this, "undeclared type: {0}", Name);
      return this;
    }

    private TypeSeq! ResolveArguments(ResolutionContext! rc) {
      TypeSeq! resolvedArgs = new TypeSeq ();
      foreach (Type! t in Arguments)
        resolvedArgs.Add(t.ResolveType(rc));
      return resolvedArgs;
    }

    public override TypeVariableSeq! FreeVariables {
      get {
         return new TypeVariableSeq ();
       }
    }

    public override List<TypeProxy!>! FreeProxies { get {
      return new List<TypeProxy!> ();
    } }

    //-----------  Linearisation  ----------------------------------

    public override void Emit(TokenTextWriter! stream, int contextBindingStrength)
    {
      stream.SetToken(this);
      // PR: should unresolved types be syntactically distinguished from resolved types?
      CtorType.EmitCtorType(this.Name, Arguments, stream, contextBindingStrength);
    }

    //-----------  Getters/Issers  ----------------------------------

    public override bool IsUnresolved { get { return true; } }
    public override UnresolvedTypeIdentifier! AsUnresolved { get { return this; } }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitUnresolvedTypeIdentifier(this);
    }
  }

  //=====================================================================

  public class TypeVariable : Type {
    public readonly string! Name;

    public TypeVariable(IToken! token, string! name)
      : base(token) 
    {
      this.Name = name;
    }

    //-----------  Cloning  ----------------------------------
    // We implement our own clone-method, because bound type variables
    // have to be created in the right way. It is /not/ ok to just clone
    // everything recursively

    public override Type! Clone(IDictionary<TypeVariable!, TypeVariable!>! varMap) {
      // if this variable is mapped to some new variable, we take the new one
      // otherwise, return this
      TypeVariable res;
      varMap.TryGetValue(this, out res);
      if (res == null)
        return this;
      else
        return res;
    }

    public override Type! CloneUnresolved() {
      return this;
    }

    //-----------  Equality  ----------------------------------

    [Pure]
    public override bool Equals(Type! that,
                                TypeVariableSeq! thisBoundVariables,
                                TypeVariableSeq! thatBoundVariables) {
      TypeVariable thatAsTypeVar = TypeProxy.FollowProxy(that.Expanded) as TypeVariable;
      
      if (thatAsTypeVar == null)
        return false;

      int thisIndex = thisBoundVariables.LastIndexOf(this);
      int thatIndex = thatBoundVariables.LastIndexOf(thatAsTypeVar);
      return (thisIndex >= 0 && thisIndex == thatIndex) ||
             (thisIndex == -1 && thatIndex == -1 &&
              Object.ReferenceEquals(this, thatAsTypeVar));
    }

    //-----------  Unification of types  -----------

    public override bool Unify(Type! that,
                               TypeVariableSeq! unifiableVariables,
                               // an idempotent substitution that describes the
                               // unification result up to a certain point
                               IDictionary<TypeVariable!, Type!>! unifier) {
      that = that.Expanded;
      if (that is TypeProxy && !(that is ConstrainedProxy))
        return that.Unify(this, unifiableVariables, unifier);

      if (this.Equals(that))
        return true;

      if (unifiableVariables.Has(this)) {
        Type previousSubst;
        unifier.TryGetValue(this, out previousSubst);
        if (previousSubst == null) {
          return addSubstitution(unifier, that);
        } else {
          // we have to unify the old instantiation with the new one
          return previousSubst.Unify(that, unifiableVariables, unifier);
        }
      }

      // this cannot be instantiated with anything
      // but that possibly can ...
      
      TypeVariable tv = that as TypeVariable;
      
      return tv != null &&
             unifiableVariables.Has(tv) &&
             that.Unify(this, unifiableVariables, unifier);
    }

    // TODO: the following might cause problems, because when applying substitutions
    // to type proxies the substitutions are not propagated to the proxy
    // constraints (right now at least)
    private bool addSubstitution(IDictionary<TypeVariable!, Type!>! oldSolution,
                                 // the type that "this" is instantiated with
                                 Type! newSubst)
      requires !oldSolution.ContainsKey(this); {

      Dictionary<TypeVariable!, Type!>! newMapping = new Dictionary<TypeVariable!, Type!> ();
      // apply the old (idempotent) substitution to the new instantiation
      Type! substSubst = newSubst.Substitute(oldSolution);
      // occurs check
      if (substSubst.FreeVariables.Has(this))
        return false;
      newMapping.Add(this, substSubst);

      // apply the new substitution to the old ones to ensure idempotence
      List<TypeVariable!>! keys = new List<TypeVariable!> ();
      keys.AddRange(oldSolution.Keys);
      foreach (TypeVariable! var in keys)
        oldSolution[var] = oldSolution[var].Substitute(newMapping);
      oldSolution.Add(this, substSubst);

      assert IsIdempotent(oldSolution);
      return true;
    }

#if OLD_UNIFICATION
    public override void Unify(Type! that,
                               TypeVariableSeq! unifiableVariables,
                               TypeVariableSeq! thisBoundVariables,
                               TypeVariableSeq! thatBoundVariables,
                               IDictionary<TypeVariable!, Type!>! result) {
      that = that.Expanded;
      int thisIndex = thisBoundVariables.LastIndexOf(this);
      if (thisIndex == -1) {
        // this is not a bound variable and can possibly be matched on that
        // that must not contain any bound variables
        TypeVariableSeq! thatFreeVars = that.FreeVariables;
        if (exists{TypeVariable! var in thatBoundVariables; thatFreeVars.Has(var)})
          throw UNIFICATION_FAILED;

        // otherwise, in case that is a typevariable it cannot be bound and
        // we can just check for equality
        if (this.Equals(that))
          return;

        if (!unifiableVariables.Has(this)) {
          // this cannot be instantiated with anything
          // but that possibly can ...
          if ((that is TypeVariable) &&
               unifiableVariables.Has(that as TypeVariable)) {
            that.Unify(this, unifiableVariables, thatBoundVariables, thisBoundVariables, result);
            return;
          } else {
            throw UNIFICATION_FAILED;
          }
        }

        Type previousSubst;
        result.TryGetValue(this, out previousSubst);
        if (previousSubst == null) {
          addSubstitution(result, that);
        } else {
          // we have to unify the old instantiation with the new one
          previousSubst.Unify(that, unifiableVariables, thisBoundVariables, thatBoundVariables, result);
        }
      } else {
        // this is a bound variable, that also has to be one (with the same index)
        if (!(that is TypeVariable) ||
            thatBoundVariables.LastIndexOf(that) != thisIndex)
          throw UNIFICATION_FAILED;
      }      
    }

#endif

    //-----------  Substitution of free variables with types not containing bound variables  -----------------

    public override Type! Substitute(IDictionary<TypeVariable!, Type!>! subst) {
      Type res;
      if (subst.TryGetValue(this, out res)) {
        assert res != null;
        return res;
      } else {
        return this;
      }
    }

    //-----------  Hashcodes  ----------------------------------

    [Pure]
    public override int GetHashCode(TypeVariableSeq! boundVariables) {
      int thisIndex = boundVariables.LastIndexOf(this);
      if (thisIndex == -1)
        return GetBaseHashCode();
      return thisIndex * 27473671;
    }

    //-----------  Linearisation  ----------------------------------

    public override void Emit(TokenTextWriter! stream, int contextBindingStrength) 
    {
      // never put parentheses around variables
      stream.SetToken(this);
      stream.Write("{0}", TokenTextWriter.SanitizeIdentifier(this.Name));
    }

    //-----------  Resolution  ----------------------------------

    public override Type! ResolveType(ResolutionContext! rc) {
      // nothing to resolve
      return this;
    }

    public override TypeVariableSeq! FreeVariables {
       get { return new TypeVariableSeq(this); }
    }

    public override List<TypeProxy!>! FreeProxies { get {
      return new List<TypeProxy!> ();
    } }

    //-----------  Getters/Issers  ----------------------------------

    public override bool IsVariable { get { return true; } }
    public override TypeVariable! AsVariable { get { return this; } }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitTypeVariable(this);
    }
  }

  //=====================================================================

  public class TypeProxy : Type {
    static int proxies = 0;
    protected readonly string! Name;
    
    public TypeProxy(IToken! token, string! givenName)
    {
      this(token, givenName, "proxy");
    }
    
    protected TypeProxy(IToken! token, string! givenName, string! kind)
    {
      Name = givenName + "$" + kind + "#" + proxies;
      proxies++;
      base(token);
    }

    private Type proxyFor;
    public Type ProxyFor {
      // apply path shortening, and then return the value of proxyFor
      get {
        TypeProxy anotherProxy = proxyFor as TypeProxy;
        if (anotherProxy != null && anotherProxy.proxyFor != null) {
          // apply path shortening by bypassing "anotherProxy" (and possibly others)
          proxyFor = anotherProxy.ProxyFor;
          assert proxyFor != null;
        }
        return proxyFor;
      }
    }
    
    [Pure][Reads(ReadsAttribute.Reads.Everything)]
    public static Type! FollowProxy(Type! t)
      ensures result is TypeProxy ==> ((TypeProxy)result).proxyFor == null;
    {
      if (t is TypeProxy) {
        Type p = ((TypeProxy)t).ProxyFor;
        if (p != null) {
          return p;
        }
      }
      return t;
    }
    
    protected void DefineProxy(Type! ty)
      requires ProxyFor == null;
    {
      // follow ty down to the leaf level, so that we can avoid creating a cycle
      ty = FollowProxy(ty);
      if (!object.ReferenceEquals(this, ty)) {
        proxyFor = ty;
      }
    }
    
    //-----------  Cloning  ----------------------------------

    public override Type! Clone(IDictionary<TypeVariable!, TypeVariable!>! varMap) {
      Type p = ProxyFor;
      if (p != null) {
        return p.Clone(varMap);
      } else {
        return new TypeProxy(this.tok, this.Name);  // the clone will have a name that ends with $proxy<n>$proxy<m>
      }
    }

    public override Type! CloneUnresolved() {
      return new TypeProxy(this.tok, this.Name);  // the clone will have a name that ends with $proxy<n>$proxy<m>
    }

    //-----------  Equality  ----------------------------------

    [Pure]
    public override bool Equals(Type! that,
                                TypeVariableSeq! thisBoundVariables,
                                TypeVariableSeq! thatBoundVariables) {
      if (object.ReferenceEquals(this, that)) {
        return true;
      }
      Type p = ProxyFor;
      if (p != null) {
        return p.Equals(that, thisBoundVariables, thatBoundVariables);
      } else {
        // This proxy could be made to be equal to anything, so what to return?
        return false;
      }
    }

    //-----------  Unification of types  -----------

    // determine whether the occurs check fails: this is a strict subtype of that
    protected bool ReallyOccursIn(Type! that) {
      that = FollowProxy(that.Expanded);
      return that.FreeProxies.Contains(this) &&
             (that.IsCtor || that.IsMap && this != that && this.ProxyFor != that);
    }

    public override bool Unify(Type! that,
                               TypeVariableSeq! unifiableVariables,
                               IDictionary<TypeVariable!, Type!>! result) {
      Type p = ProxyFor;
      if (p != null) {
        return p.Unify(that, unifiableVariables, result);
      } else {
        // unify this with that
        if (this.ReallyOccursIn(that))
          return false;
        DefineProxy(that.Expanded);
        return true;
      }
    }

    //-----------  Substitution of free variables with types not containing bound variables  -----------------

    public override Type! Substitute(IDictionary<TypeVariable!, Type!>! subst) {
      Type p = ProxyFor;
      if (p != null) {
        return p.Substitute(subst);
      } else {
        return this;
      }
    }

    //-----------  Hashcodes  ----------------------------------

    [Pure]
    public override int GetHashCode(TypeVariableSeq! boundVariables) {
      Type p = ProxyFor;
      if (p != null) {
        return p.GetHashCode(boundVariables);
      } else {
        return GetBaseHashCode();
      }
    }

    //-----------  Linearisation  ----------------------------------

    public override void Emit(TokenTextWriter! stream, int contextBindingStrength) 
    {
      Type p = ProxyFor;
      if (p != null) {
        p.Emit(stream, contextBindingStrength);
      } else {
        // no need for parentheses
        stream.SetToken(this);
        stream.Write("{0}", TokenTextWriter.SanitizeIdentifier(this.Name));
      }
    }

    //-----------  Resolution  ----------------------------------

    public override Type! ResolveType(ResolutionContext! rc) {
      Type p = ProxyFor;
      if (p != null) {
        return p.ResolveType(rc);
      } else {
        return this;
      }
    }

    public override TypeVariableSeq! FreeVariables {
       get {
         Type p = ProxyFor;
         if (p != null) {
           return p.FreeVariables;
         } else {
           return new TypeVariableSeq();
         }
       }
    }

    public override List<TypeProxy!>! FreeProxies { get {
       Type p = ProxyFor;
       if (p != null) {
         return p.FreeProxies;
       } else {
         List<TypeProxy!>! res = new List<TypeProxy!> ();
         res.Add(this);
         return res;
       }
    } }

    //-----------  Getters/Issers  ----------------------------------

    public override bool IsBasic { get {
      Type p = ProxyFor;
      return p != null && p.IsBasic;
    } }
    public override bool IsInt { get {
      Type p = ProxyFor;
      return p != null && p.IsInt;
    } }
    public override bool IsBool { get {
      Type p = ProxyFor;
      return p != null && p.IsBool;
    } }

    public override bool IsVariable { get {
      Type p = ProxyFor;
      return p != null && p.IsVariable;
    } }
    public override TypeVariable! AsVariable { get {
      Type p = ProxyFor;
      assume p != null;
      return p.AsVariable;
    } }

    public override bool IsCtor { get {
      Type p = ProxyFor;
      return p != null && p.IsCtor;
    } }
    public override CtorType! AsCtor { get {
      Type p = ProxyFor;
      assume p != null;
      return p.AsCtor;
    } }
    public override bool IsMap { get {
      Type p = ProxyFor;
      return p != null && p.IsMap;
    } }
    public override MapType! AsMap { get {
      Type p = ProxyFor;
      assume p != null;
      return p.AsMap;
    } }
    public override int MapArity { get {
      Type p = ProxyFor;
      assume p != null;
      return p.MapArity;
    } }
    public override bool IsUnresolved { get {
      Type p = ProxyFor;
      return p != null && p.IsUnresolved;
    } }
    public override UnresolvedTypeIdentifier! AsUnresolved { get {
      Type p = ProxyFor;
      assume p != null;
      return p.AsUnresolved;
    } }

    public override bool IsBv { get {
      Type p = ProxyFor;
      return p != null && p.IsBv;
    } }
    public override int BvBits { get {
      Type p = ProxyFor;
      assume p != null;
      return p.BvBits;
    } }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitTypeProxy(this);
    }
  }

  public abstract class ConstrainedProxy : TypeProxy {
    protected ConstrainedProxy(IToken! token, string! givenName, string! kind) {
      base(token, givenName, kind);
    }
  }
  
  /// <summary>
  /// Each instance of this class represents a set of bitvector types.  In particular, it represents
  /// a bitvector type bvN iff
  ///   minBits ATMOST N  and
  ///   foreach constraint (t0,t1), the types represented by t0 and t1 are bitvector types whose
  ///   number of bits add up to N.
  /// This means that the size of a BvTypeProxy p is constrained not only by p.minBits, but also
  /// by the size of various t0 and t1 types that are transitively part of BvTypeProxy constraints.
  /// If such a t0 or t1 were to get its ProxyFor field defined, then p would have to be further
  /// constrained too.  This doesn't seem like it would ever occur in a Boogie 2 program, because:
  ///   the only place where a BvTypeProxy with constraints can occur is as the type of a
  ///   BvConcatExpr, and
  ///   the types of all local variables are explicitly declared, which means that the types of
  ///   subexpressions of a BvConcatExpr are not going to change other than via the type of the
  ///   BvConcatExpr.
  /// So, this implementation of BvTypeProxy does not keep track of where a BvTypeProxy may occur
  /// transitively in some other BvTypeProxy's constraints.
  /// </summary>
  public class BvTypeProxy : ConstrainedProxy {
    public int MinBits;
    List<BvTypeConstraint!> constraints;
    class BvTypeConstraint {
      public Type! T0;
      public Type! T1;
      public BvTypeConstraint(Type! t0, Type! t1)
        requires t0.IsBv && t1.IsBv;
      {
        T0 = t0;
        T1 = t1;
      }
    }
    
    public BvTypeProxy(IToken! token, string! name, int minBits)
    {
      base(token, name, "bv" + minBits + "proxy");
      this.MinBits = minBits;
    }

    /// <summary>
    /// Requires that any further constraints to be placed on t0 and t1 go via the object to
    /// be constructed.
    /// </summary>
    public BvTypeProxy(IToken! token, string! name, Type! t0, Type! t1)
      requires t0.IsBv && t1.IsBv;
    {
      base(token, name, "bvproxy");
      t0 = FollowProxy(t0);
      t1 = FollowProxy(t1);
      this.MinBits = MinBitsFor(t0) + MinBitsFor(t1);
      List<BvTypeConstraint!> list = new List<BvTypeConstraint!>();
      list.Add(new BvTypeConstraint(t0, t1));
      this.constraints = list;
    }
    
    /// <summary>
    /// Construct a BvTypeProxy like p, but with minBits.
    /// </summary>
    private BvTypeProxy(BvTypeProxy! p, int minBits)
    {
      base(p.tok, p.Name, "");
      this.MinBits = minBits;
      this.constraints = p.constraints;
    }
    
    private BvTypeProxy(IToken! token, string! name, int minBits, List<BvTypeConstraint!> constraints) {
      base(token, name, "");
      this.MinBits = minBits;
      this.constraints = constraints;
    }
    
    [Pure][Reads(ReadsAttribute.Reads.Everything)]
    private static int MinBitsFor(Type! t)
      requires t.IsBv;
      ensures 0 <= result;
    {
      if (t is BvType) {
        return t.BvBits;
      } else {
        return ((BvTypeProxy)t).MinBits;
      }
    }

    //-----------  Cloning  ----------------------------------

    public override Type! Clone(IDictionary<TypeVariable!, TypeVariable!>! varMap) {
      Type p = ProxyFor;
      if (p != null) {
        return p.Clone(varMap);
      } else {
        return new BvTypeProxy(this.tok, this.Name, this.MinBits, this.constraints);  // the clone will have a name that ends with $bvproxy<n>$bvproxy<m>
      }
    }

    public override Type! CloneUnresolved() {
      return new BvTypeProxy(this.tok, this.Name, this.MinBits, this.constraints);  // the clone will have a name that ends with $bvproxy<n>$bvproxy<m>
    }

    //-----------  Unification of types  -----------

    public override bool Unify(Type! that,
                               TypeVariableSeq! unifiableVariables,
                               IDictionary<TypeVariable!, Type!>! result) {
      Type p = ProxyFor;
      if (p != null) {
        return p.Unify(that, unifiableVariables, result);
      }

      // unify this with that, if possible
      that = that.Expanded;
      that = FollowProxy(that);

      if (this.ReallyOccursIn(that))
        return false;

      TypeVariable tv = that as TypeVariable;

      if (tv != null && unifiableVariables.Has(tv))
        return that.Unify(this, unifiableVariables, result);

      if (object.ReferenceEquals(this, that)) {
        return true;
      } else if (that is BvType) {
        if (MinBits <= that.BvBits) {
          if (constraints != null) {
            foreach (BvTypeConstraint btc in constraints) {
              int minT1 = MinBitsFor(btc.T1);
              int left = IncreaseBits(btc.T0, that.BvBits - minT1);
              left = IncreaseBits(btc.T1, minT1 + left);
              assert left == 0;  // because it should always be possible to increase the total size of a BvTypeConstraint pair (t0,t1) arbitrarily
            }
          }
          DefineProxy(that);
          return true;
        }
      } else if (that is BvTypeProxy) {
        BvTypeProxy bt = (BvTypeProxy)that;
        // keep the proxy with the stronger constraint (that is, the higher minBits), but if either
        // has a constraints list, then concatenate both constraints lists and define the previous
        // proxies to the new one
        if (this.constraints != null || bt.constraints != null) {
          List<BvTypeConstraint!> list = new List<BvTypeConstraint!>();
          if (this.constraints != null) { list.AddRange(this.constraints); }
          if (bt.constraints != null) { list.AddRange(bt.constraints); }
          BvTypeProxy np = new BvTypeProxy(this.tok, this.Name, max{this.MinBits, bt.MinBits}, list);
          this.DefineProxy(np);
          bt.DefineProxy(np);
        } else if (this.MinBits <= bt.MinBits) {
          this.DefineProxy(bt);
        } else {
          bt.DefineProxy(this);
        }
        return true;
      } else if (that is ConstrainedProxy) {
        // only bitvector proxies can be unified with this BvTypeProxy
        return false;
      } else if (that is TypeProxy) {
        // define:  that.ProxyFor := this;
        return that.Unify(this, unifiableVariables, result);
      }
      return false;
    }

    private static int IncreaseBits(Type! t, int to)
      requires t.IsBv && 0 <= to && MinBitsFor(t) <= to;
      ensures 0 <= result && result <= to;
    {
      t = FollowProxy(t);
      if (t is BvType) {
        return to - t.BvBits;
      } else {
        BvTypeProxy p = (BvTypeProxy)t;
        assert p.MinBits <= to;
        if (p.MinBits < to) {
          BvTypeProxy q = new BvTypeProxy(p, to);
          p.DefineProxy(q);
        }
        return 0;  // we were able to satisfy the request completely
      }
    }

    //-----------  Substitution of free variables with types not containing bound variables  -----------------

    public override Type! Substitute(IDictionary<TypeVariable!, Type!>! subst) {
      if (this.ProxyFor == null) {
        // check that the constraints are clean and do not contain any
        // of the substituted variables (otherwise, we are in big trouble)
        assert forall{BvTypeConstraint! c in constraints;
               forall{TypeVariable! var in subst.Keys;
                      !c.T0.FreeVariables.Has(var) && !c.T1.FreeVariables.Has(var)}};
      }
      return base.Substitute(subst);
    }
    
    //-----------  Getters/Issers  ----------------------------------

    public override bool IsBv { get {
      return true;
    } }
    public override int BvBits { get {
      // This method is supposed to return the number of bits supplied, but unless the proxy has been resolved,
      // we only have a lower bound on the number of bits supplied.  But this method is not supposed to be
      // called until type checking has finished, at which time the minBits is stable.
      Type p = ProxyFor;
      if (p != null) {
        return p.BvBits;
      } else {
        return MinBits;
      }
    } }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitBvTypeProxy(this);
    }
  }

  // Proxy representing map types with a certain arity. Apart from the arity,
  // a number of constraints on the index and value type of the map type may
  // be known (such constraints result from applied select and store operations).
  // Because map type can be polymorphic (in the most general case, each index or
  // value type is described by a separate type parameter) any combination of
  // constraints can be satisfied.
  public class MapTypeProxy : ConstrainedProxy {
    public readonly int Arity;
    private readonly List<Constraint>! constraints = new List<Constraint> ();

    // each constraint specifies that the given combination of argument/result
    // types must be a possible instance of the formal map argument/result types
    private struct Constraint {
      public readonly TypeSeq! Arguments;
      public readonly Type! Result;

      public Constraint(TypeSeq! arguments, Type! result) {
        Arguments = arguments;
        Result = result;
      }

      public Constraint Clone(IDictionary<TypeVariable!, TypeVariable!>! varMap) {
        TypeSeq! args = new TypeSeq ();
        foreach (Type! t in Arguments)
          args.Add(t.Clone(varMap));
        Type! res = Result.Clone(varMap);
        return new Constraint(args, res);
      }

      public bool Unify(MapType! that,
                        TypeVariableSeq! unifiableVariables,
                        IDictionary<TypeVariable!, Type!>! result)
        requires Arguments.Length == that.Arguments.Length; {
        Dictionary<TypeVariable!, Type!>! subst = new Dictionary<TypeVariable!, Type!>();
        foreach (TypeVariable! tv in that.TypeParameters) {
          TypeProxy proxy = new TypeProxy(Token.NoToken, tv.Name);
          subst.Add(tv, proxy);
        }
          
        bool good = true;
        for (int i = 0; i < that.Arguments.Length; i++) {
          Type t0 = that.Arguments[i].Substitute(subst);
          Type t1 = this.Arguments[i];
          good &= t0.Unify(t1, unifiableVariables, result);
        }
        good &= that.Result.Substitute(subst).Unify(this.Result, unifiableVariables, result);
        return good;
      }
    }

    public MapTypeProxy(IToken! token, string! name, int arity)
      requires 0 <= arity; {
      base(token, name, "mapproxy");
      this.Arity = arity;
    }

    private void AddConstraint(Constraint c)
      requires c.Arguments.Length == Arity; {

      Type f = ProxyFor;
      MapType mf = f as MapType;
      if (mf != null) {
        bool success = c.Unify(mf, new TypeVariableSeq(), new Dictionary<TypeVariable!, Type!> ());
        assert success;
        return;
      }

      MapTypeProxy mpf = f as MapTypeProxy;
      if (mpf != null) {
        mpf.AddConstraint(c);
        return;
      }
      
      assert f == null;  // no other types should occur as specialisations of this proxy

      constraints.Add(c);
    }

    public Type CheckArgumentTypes(ExprSeq! actualArgs,
                                   out TypeParamInstantiation! tpInstantiation,
                                   IToken! typeCheckingSubject,
                                   string! opName,
                                   TypecheckingContext! tc)
    {
      Type f = ProxyFor;
      MapType mf = f as MapType;
      if (mf != null)
        return mf.CheckArgumentTypes(actualArgs, out tpInstantiation, typeCheckingSubject, opName, tc);

      MapTypeProxy mpf = f as MapTypeProxy;
      if (mpf != null)
        return mpf.CheckArgumentTypes(actualArgs, out tpInstantiation, typeCheckingSubject, opName, tc);

      assert f == null;  // no other types should occur as specialisations of this proxy

      // otherwise, we just record the constraints given by this usage of the map type
      TypeSeq! arguments = new TypeSeq ();
      foreach (Expr! e in actualArgs)
        arguments.Add(e.Type);
      Type! result = new TypeProxy (tok, "result");
      AddConstraint(new Constraint (arguments, result));

      TypeSeq! argumentsResult = new TypeSeq ();
      foreach (Expr! e in actualArgs)
        argumentsResult.Add(e.Type);
      argumentsResult.Add(result);
      
      tpInstantiation = new MapTypeProxyParamInstantiation(this, argumentsResult);
      return result;
    }

    //-----------  Cloning  ----------------------------------

    public override Type! Clone(IDictionary<TypeVariable!, TypeVariable!>! varMap) {
      Type p = ProxyFor;
      if (p != null) {
        return p.Clone(varMap);
      } else {
        MapTypeProxy p2 = new MapTypeProxy(tok, Name, Arity);
        foreach (Constraint c in constraints)
          p2.AddConstraint(c.Clone(varMap));
        return p2;  // the clone will have a name that ends with $mapproxy<n>$mapproxy<m> (hopefully)
      }
    }

    //-----------  Linearisation  ----------------------------------

    public override void Emit(TokenTextWriter! stream, int contextBindingStrength) 
    {
      Type p = ProxyFor;
      if (p != null) {
        p.Emit(stream, contextBindingStrength);
      } else {
        stream.Write("[");
        string! sep = "";
        for (int i = 0; i < Arity; ++i) {
          stream.Write(sep);
          sep = ", ";
          stream.Write("?");
        }
        stream.Write("]?");
      }
    }

    //-----------  Unification of types  -----------

    public override bool Unify(Type! that,
                               TypeVariableSeq! unifiableVariables,
                               IDictionary<TypeVariable!, Type!>! result) {
      Type p = ProxyFor;
      if (p != null) {
        return p.Unify(that, unifiableVariables, result);
      }

      // unify this with that, if possible
      that = that.Expanded;
      that = FollowProxy(that);

      if (this.ReallyOccursIn(that))
        return false;

      TypeVariable tv = that as TypeVariable;

      if (tv != null  && unifiableVariables.Has(tv))
        return that.Unify(this, unifiableVariables, result);

      if (object.ReferenceEquals(this, that)) {
        return true;
      } else if (that is MapType) {
        MapType mapType = (MapType)that;
        if (mapType.Arguments.Length == Arity) {
          bool good = true;
          foreach (Constraint c in constraints)
            good &= c.Unify(mapType, unifiableVariables, result);
          if (good) {
            DefineProxy(mapType);
            return true;
          }
        }
      } else if (that is MapTypeProxy) {
        MapTypeProxy mt = (MapTypeProxy)that;
        if (mt.Arity == this.Arity) {
          // we propagate the constraints of this proxy to the more specific one
          foreach (Constraint c in constraints)
            mt.AddConstraint(c);
          DefineProxy(mt);
          return true;
        }
      } else if (that is ConstrainedProxy) {
        // only map-type proxies can be unified with this MapTypeProxy
        return false;
      } else if (that is TypeProxy) {
        // define:  that.ProxyFor := this;
        return that.Unify(this, unifiableVariables, result);
      }
      return false;
    }

    //-----------  Substitution of free variables with types not containing bound variables  -----------------

    public override Type! Substitute(IDictionary<TypeVariable!, Type!>! subst) {
      if (this.ProxyFor == null) {
        // check that the constraints are clean and do not contain any
        // of the substituted variables (otherwise, we are in big trouble)
        assert forall{Constraint c in constraints;
               forall{TypeVariable! var in subst.Keys;
               forall{Type! t in c.Arguments; !t.FreeVariables.Has(var)} &&
                     !c.Result.FreeVariables.Has(var)}};
      }
      return base.Substitute(subst);
    }

    //-----------  Getters/Issers  ----------------------------------

    public override bool IsMap { get { return true; } }
    public override MapType! AsMap { get {
      Type p = ProxyFor;
      if (p != null) {
        return p.AsMap;
      } else {
        assert false;  // what to do now?
      }
    } }
    public override int MapArity { get {
      return Arity;
    } }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitMapTypeProxy(this);
    }
  }

  //=====================================================================

  // Used to annotate types with type synoyms that were used in the
  // original unresolved types. Such types should be considered as
  // equivalent to ExpandedType, the annotations are only used to enable
  // better pretty-printing
  public class TypeSynonymAnnotation : Type {
    public Type! ExpandedType;

    public readonly TypeSeq! Arguments;
    // is set during resolution and determines whether the right number of arguments is given
    public readonly TypeSynonymDecl! Decl;

    public TypeSynonymAnnotation(IToken! token, TypeSynonymDecl! decl, TypeSeq! arguments)
      : base(token) 
      requires arguments.Length == decl.TypeParameters.Length;
    {
      this.Decl = decl;
      this.Arguments = arguments;

      // build a substitution that can be applied to the definition of
      // the type synonym
      IDictionary<TypeVariable!, Type!>! subst =
        new Dictionary<TypeVariable!, Type!> ();
      for (int i = 0; i < arguments.Length; ++i)
        subst.Add(decl.TypeParameters[i], arguments[i]);

      ExpandedType = decl.Body.Substitute(subst);
    }

    private TypeSynonymAnnotation(IToken! token, TypeSynonymDecl! decl, TypeSeq! arguments,
                                  Type! expandedType)
      : base(token) {
      this.Decl = decl;
      this.Arguments = arguments;
      this.ExpandedType = expandedType;
    }

    //-----------  Cloning  ----------------------------------
    // We implement our own clone-method, because bound type variables
    // have to be created in the right way. It is /not/ ok to just clone
    // everything recursively

    public override Type! Clone(IDictionary<TypeVariable!, TypeVariable!>! varMap) {
      TypeSeq! newArgs = new TypeSeq ();
      foreach(Type! t in Arguments)
        newArgs.Add(t.Clone(varMap));
      Type! newExpandedType = ExpandedType.Clone(varMap);
      return new TypeSynonymAnnotation(tok, Decl, newArgs, newExpandedType);
    }

    public override Type! CloneUnresolved() {
      TypeSeq! newArgs = new TypeSeq ();
      foreach(Type! t in Arguments)
        newArgs.Add(t.CloneUnresolved());
      return new TypeSynonymAnnotation(tok, Decl, newArgs);
    }

    //-----------  Equality  ----------------------------------

    [Pure]
    public override bool Equals(Type! that,
                                TypeVariableSeq! thisBoundVariables,
                                TypeVariableSeq! thatBoundVariables) {
      return ExpandedType.Equals(that, thisBoundVariables, thatBoundVariables);
    }

    // used to skip leading type annotations
    internal override Type! Expanded { get {
      return ExpandedType.Expanded;
    } }

    //-----------  Unification of types  -----------

    public override bool Unify(Type! that,
                               TypeVariableSeq! unifiableVariables,
                               IDictionary<TypeVariable!, Type!>! result) {
      return ExpandedType.Unify(that, unifiableVariables, result);
    }

#if OLD_UNIFICATION
    public override void Unify(Type! that,
                               TypeVariableSeq! unifiableVariables,
                               TypeVariableSeq! thisBoundVariables,
                               TypeVariableSeq! thatBoundVariables,
                               IDictionary<TypeVariable!, Type!>! result) {
      ExpandedType.Unify(that, unifiableVariables,
                         thisBoundVariables, thatBoundVariables, result);
    }
#endif

    //-----------  Substitution of free variables with types not containing bound variables  -----------------

    public override Type! Substitute(IDictionary<TypeVariable!, Type!>! subst) {
      if (subst.Count == 0)
        return this;
      TypeSeq newArgs = new TypeSeq ();
      foreach (Type! t in Arguments)
        newArgs.Add(t.Substitute(subst));
      Type! newExpandedType = ExpandedType.Substitute(subst);
      return new TypeSynonymAnnotation(tok, Decl, newArgs, newExpandedType);
    }

    //-----------  Hashcodes  ----------------------------------

    [Pure]
    public override int GetHashCode(TypeVariableSeq! boundVariables) {
      return ExpandedType.GetHashCode(boundVariables);
    }

    //-----------  Linearisation  ----------------------------------

    public override void Emit(TokenTextWriter! stream, int contextBindingStrength) 
    {
      stream.SetToken(this);
      CtorType.EmitCtorType(this.Decl.Name, Arguments, stream, contextBindingStrength);
    }

    //-----------  Resolution  ----------------------------------

    public override Type! ResolveType(ResolutionContext! rc) {
      TypeSeq resolvedArgs = new TypeSeq ();
      foreach (Type! t in Arguments)
        resolvedArgs.Add(t.ResolveType(rc));
      return new TypeSynonymAnnotation(tok, Decl, resolvedArgs);
    }

    public override TypeVariableSeq! FreeVariables { get {
      return ExpandedType.FreeVariables;
    } }

    public override List<TypeProxy!>! FreeProxies { get {
      return ExpandedType.FreeProxies;
    } }

    //-----------  Getters/Issers  ----------------------------------

    public override bool IsBasic { get { return ExpandedType.IsBasic; } }
    public override bool IsInt { get { return ExpandedType.IsInt; } }
    public override bool IsBool { get { return ExpandedType.IsBool; } }

    public override bool IsVariable { get { return ExpandedType.IsVariable; } }
    public override TypeVariable! AsVariable { get { return ExpandedType.AsVariable; } }
    public override bool IsCtor { get { return ExpandedType.IsCtor; } }
    public override CtorType! AsCtor { get { return ExpandedType.AsCtor; } }
    public override bool IsMap { get { return ExpandedType.IsMap; } }
    public override MapType! AsMap { get { return ExpandedType.AsMap; } }
    public override bool IsUnresolved { get { return ExpandedType.IsUnresolved; } }
    public override UnresolvedTypeIdentifier! AsUnresolved { get {
        return ExpandedType.AsUnresolved; } }

    public override bool IsBv { get { return ExpandedType.IsBv; } }
    public override int BvBits { get { return ExpandedType.BvBits; } }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitTypeSynonymAnnotation(this);
    }
  }

  //=====================================================================

  public class CtorType : Type {
    public readonly TypeSeq! Arguments;
    // is set during resolution and determines whether the right number of arguments is given
    public readonly TypeCtorDecl! Decl;

    public CtorType(IToken! token, TypeCtorDecl! decl, TypeSeq! arguments)
      : base(token) 
      requires arguments.Length == decl.Arity;
    {
      this.Decl = decl;
      this.Arguments = arguments;
    }

    //-----------  Cloning  ----------------------------------
    // We implement our own clone-method, because bound type variables
    // have to be created in the right way. It is /not/ ok to just clone
    // everything recursively

    public override Type! Clone(IDictionary<TypeVariable!, TypeVariable!>! varMap) {
      TypeSeq! newArgs = new TypeSeq ();
      foreach(Type! t in Arguments)
        newArgs.Add(t.Clone(varMap));
      return new CtorType(tok, Decl, newArgs);
    }

    public override Type! CloneUnresolved() {
      TypeSeq! newArgs = new TypeSeq ();
      foreach(Type! t in Arguments)
        newArgs.Add(t.CloneUnresolved());
      return new CtorType(tok, Decl, newArgs);
    }

    //-----------  Equality  ----------------------------------

    [Pure][Reads(ReadsAttribute.Reads.Nothing)]
    public override bool Equals(object that) {
      Type thatType = that as Type;
      if (thatType == null)
        return false;
      thatType = TypeProxy.FollowProxy(thatType.Expanded);
      // shortcut
      CtorType thatCtorType = thatType as CtorType;
      if (thatCtorType == null || !this.Decl.Equals(thatCtorType.Decl))
        return false;
      if (Arguments.Length == 0)
        return true;
      return base.Equals(thatType);
    }

    [Pure]
    public override bool Equals(Type! that,
                                TypeVariableSeq! thisBoundVariables,
                                TypeVariableSeq! thatBoundVariables) {
      that = TypeProxy.FollowProxy(that.Expanded);
      CtorType thatCtorType = that as CtorType;
      if (thatCtorType == null || !this.Decl.Equals(thatCtorType.Decl))
        return false;
      for (int i = 0; i < Arguments.Length; ++i) {
        if (!Arguments[i].Equals(thatCtorType.Arguments[i],
                                 thisBoundVariables, thatBoundVariables))
          return false;
      }
      return true;
    }

    //-----------  Unification of types  -----------

    public override bool Unify(Type! that,
                               TypeVariableSeq! unifiableVariables,
                               IDictionary<TypeVariable!, Type!>! result) {
      that = that.Expanded;
      if (that is TypeProxy || that is TypeVariable)
        return that.Unify(this, unifiableVariables, result);

      CtorType thatCtorType = that as CtorType;
      if (thatCtorType == null || !thatCtorType.Decl.Equals(Decl)) {
        return false;
      } else {
        bool good = true;
        for (int i = 0; i < Arguments.Length; ++i)
          good &= Arguments[i].Unify(thatCtorType.Arguments[i], unifiableVariables, result);
        return good;
      }
    }

#if OLD_UNIFICATION
    public override void Unify(Type! that,
                               TypeVariableSeq! unifiableVariables,
                               TypeVariableSeq! thisBoundVariables,
                               TypeVariableSeq! thatBoundVariables,
                               IDictionary<TypeVariable!, Type!>! result) {
      that = that.Expanded;
      if (that is TypeVariable) {
        that.Unify(this, unifiableVariables, thatBoundVariables, thisBoundVariables, result);
        return;
      }

      CtorType thatCtorType = that as CtorType;
      if (thatCtorType == null || !thatCtorType.Decl.Equals(Decl))
        throw UNIFICATION_FAILED;
      for (int i = 0; i < Arguments.Length; ++i)
        Arguments[i].Unify(thatCtorType.Arguments[i],
                           unifiableVariables,
                           thisBoundVariables, thatBoundVariables,
                           result);
    }
#endif

    //-----------  Substitution of free variables with types not containing bound variables  -----------------

    public override Type! Substitute(IDictionary<TypeVariable!, Type!>! subst) {
      if (subst.Count == 0)
        return this;
      TypeSeq newArgs = new TypeSeq ();
      foreach (Type! t in Arguments)
        newArgs.Add(t.Substitute(subst));
      return new CtorType(tok, Decl, newArgs);
    }

    //-----------  Hashcodes  ----------------------------------

    [Pure]
    public override int GetHashCode(TypeVariableSeq! boundVariables) {
      int res = 1637643879 * Decl.GetHashCode();
      foreach (Type! t in Arguments)
        res = res * 3 + t.GetHashCode(boundVariables);
      return res;
    }

    //-----------  Linearisation  ----------------------------------

    public override void Emit(TokenTextWriter! stream, int contextBindingStrength) 
    {
      stream.SetToken(this);
      EmitCtorType(this.Decl.Name, Arguments, stream, contextBindingStrength);
    }
    
    internal static void EmitCtorType(string! name, TypeSeq! args, TokenTextWriter! stream, int contextBindingStrength) {
      int opBindingStrength = args.Length > 0 ? 0 : 2;
      if (opBindingStrength < contextBindingStrength)
        stream.Write("(");

      stream.Write("{0}", TokenTextWriter.SanitizeIdentifier(name));
      int i = args.Length;
      foreach (Type! t in args) {
        stream.Write(" ");
        // use a lower binding strength for the last argument
        // to allow map-types without parentheses
        t.Emit(stream, i == 1 ? 1 : 2);
        i = i - 1;
      }

      if (opBindingStrength < contextBindingStrength)
        stream.Write(")");
    }

    //-----------  Resolution  ----------------------------------

    public override Type! ResolveType(ResolutionContext! rc) {
      TypeSeq resolvedArgs = new TypeSeq ();
      foreach (Type! t in Arguments)
        resolvedArgs.Add(t.ResolveType(rc));
      return new CtorType(tok, Decl, resolvedArgs);
    }

    public override TypeVariableSeq! FreeVariables {
       get {
         TypeVariableSeq! res = new TypeVariableSeq ();
         foreach (Type! t in Arguments)
           res.AppendWithoutDups(t.FreeVariables);
         return res;
       }
    }

    public override List<TypeProxy!>! FreeProxies { get {
      List<TypeProxy!>! res = new List<TypeProxy!> ();
      foreach (Type! t in Arguments)
        AppendWithoutDups(res, t.FreeProxies);
      return res;
    } }

    //-----------  Getters/Issers  ----------------------------------

    public override bool IsCtor { get { return true; } }
    public override CtorType! AsCtor { get { return this; } }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitCtorType(this);
    }
  }

  //=====================================================================

  public class MapType : Type {
    // an invariant is that each of the type parameters has to occur as
    // free variable in at least one of the arguments
    public readonly TypeVariableSeq! TypeParameters;
    public readonly TypeSeq! Arguments;
    public Type! Result;

    public MapType(IToken! token, TypeVariableSeq! typeParameters, TypeSeq! arguments, Type! result)
      : base(token) 
    {
      this.TypeParameters = typeParameters;
      this.Result = result;
      this.Arguments = arguments;
    }

    //-----------  Cloning  ----------------------------------
    // We implement our own clone-method, because bound type variables
    // have to be created in the right way. It is /not/ ok to just clone
    // everything recursively

    public override Type! Clone(IDictionary<TypeVariable!, TypeVariable!>! varMap) {
      IDictionary<TypeVariable!, TypeVariable!>! newVarMap =
        new Dictionary<TypeVariable!, TypeVariable!>();
      foreach (KeyValuePair<TypeVariable!, TypeVariable!> p in varMap) {
        if (!TypeParameters.Has(p.Key))
          newVarMap.Add(p);
      }

      TypeVariableSeq! newTypeParams = new TypeVariableSeq ();
      foreach (TypeVariable! var in TypeParameters) {
        TypeVariable! newVar = new TypeVariable (var.tok, var.Name);
        newVarMap.Add(var, newVar);
        newTypeParams.Add(newVar);
      }

      TypeSeq! newArgs = new TypeSeq ();
      foreach(Type! t in Arguments)
        newArgs.Add(t.Clone(newVarMap));
      Type! newResult = Result.Clone(newVarMap);

      return new MapType (this.tok, newTypeParams, newArgs, newResult);
    }

    public override Type! CloneUnresolved() {
      TypeVariableSeq! newTypeParams = new TypeVariableSeq ();
      foreach (TypeVariable! var in TypeParameters) {
        TypeVariable! newVar = new TypeVariable (var.tok, var.Name);
        newTypeParams.Add(newVar);
      }

      TypeSeq! newArgs = new TypeSeq ();
      foreach(Type! t in Arguments)
        newArgs.Add(t.CloneUnresolved());
      Type! newResult = Result.CloneUnresolved();

      return new MapType (this.tok, newTypeParams, newArgs, newResult);
    }

    //-----------  Equality  ----------------------------------

    [Pure]
    public override bool Equals(Type! that,
                                TypeVariableSeq! thisBoundVariables,
                                TypeVariableSeq! thatBoundVariables) {
      that = TypeProxy.FollowProxy(that.Expanded);
      MapType thatMapType = that as MapType;
      if (thatMapType == null ||
          this.TypeParameters.Length != thatMapType.TypeParameters.Length ||
          this.Arguments.Length != thatMapType.Arguments.Length)
        return false;

      foreach (TypeVariable! var in this.TypeParameters)
        thisBoundVariables.Add(var);
      foreach (TypeVariable! var in thatMapType.TypeParameters)
        thatBoundVariables.Add(var);

      try {

        for (int i = 0; i < Arguments.Length; ++i) {
          if (!Arguments[i].Equals(thatMapType.Arguments[i],
                                   thisBoundVariables, thatBoundVariables))
            return false;
        }
        if (!this.Result.Equals(thatMapType.Result,
                                thisBoundVariables, thatBoundVariables))
          return false;

      } finally {
        // make sure that the bound variables are removed again
        for (int i = 0; i < this.TypeParameters.Length; ++i) {
          thisBoundVariables.Remove();
          thatBoundVariables.Remove();
        }
      }
      
      return true;
    }

    //-----------  Unification of types  -----------

    public override bool Unify(Type! that,
                               TypeVariableSeq! unifiableVariables,
                               IDictionary<TypeVariable!, Type!>! result) {
      that = that.Expanded;
      if (that is TypeProxy || that is TypeVariable)
        return that.Unify(this, unifiableVariables, result);

      MapType thatMapType = that as MapType;
      if (thatMapType == null ||
          this.TypeParameters.Length != thatMapType.TypeParameters.Length ||
          this.Arguments.Length != thatMapType.Arguments.Length)
        return false;

      // treat the bound variables of the two map types as equal...
      Dictionary<TypeVariable!, Type!>! subst0 = new Dictionary<TypeVariable!, Type!>();
      Dictionary<TypeVariable!, Type!>! subst1 = new Dictionary<TypeVariable!, Type!>();
      TypeVariableSeq freshies = new TypeVariableSeq();
      for (int i = 0; i < this.TypeParameters.Length; i++) {
        TypeVariable tp0 = this.TypeParameters[i];
        TypeVariable tp1 = thatMapType.TypeParameters[i];
        TypeVariable freshVar = new TypeVariable(tp0.tok, tp0.Name);
        freshies.Add(freshVar);
        subst0.Add(tp0, freshVar);
        subst1.Add(tp1, freshVar);
      }
      // ... and then unify the domain and range types
      bool good = true;
      for (int i = 0; i < this.Arguments.Length; i++) {
        Type t0 = this.Arguments[i].Substitute(subst0);
        Type t1 = thatMapType.Arguments[i].Substitute(subst1);
        good &= t0.Unify(t1, unifiableVariables, result);
      }
      Type r0 = this.Result.Substitute(subst0);
      Type r1 = thatMapType.Result.Substitute(subst1);
      good &= r0.Unify(r1, unifiableVariables, result);

      // Finally, check that none of the bound variables has escaped
      if (good && freshies.Length != 0) {
        // This is done by looking for occurrences of the fresh variables in the
        // non-substituted types ...
        TypeVariableSeq freeVars = this.FreeVariables;
        foreach (TypeVariable fr in freshies)
          if (freeVars.Has(fr)) { return false; }  // fresh variable escaped
        freeVars = thatMapType.FreeVariables;
        foreach (TypeVariable fr in freshies)
          if (freeVars.Has(fr)) { return false; }  // fresh variable escaped

        // ... and in the resulting unifier of type variables
        foreach (KeyValuePair<TypeVariable!, Type!> pair in result) {
          freeVars = pair.Value.FreeVariables;
          foreach (TypeVariable fr in freshies)
            if (freeVars.Has(fr)) { return false; }  // fresh variable escaped          
        }
      }

      return good;
    }

#if OLD_UNIFICATION
    public override void Unify(Type! that,
                               TypeVariableSeq! unifiableVariables,
                               TypeVariableSeq! thisBoundVariables,
                               TypeVariableSeq! thatBoundVariables,
                               IDictionary<TypeVariable!, Type!>! result) {
      that = that.Expanded;
      if (that is TypeVariable) {
        that.Unify(this, unifiableVariables, thatBoundVariables, thisBoundVariables, result);
        return;
      }

      MapType thatMapType = that as MapType;
      if (thatMapType == null ||
          this.TypeParameters.Length != thatMapType.TypeParameters.Length ||
          this.Arguments.Length != thatMapType.Arguments.Length)
        throw UNIFICATION_FAILED;

      // ensure that no collisions occur
      if (this.collisionsPossible(result)) {
        ((MapType)this.Clone())
          .Unify(that, unifiableVariables,
                 thisBoundVariables, thatBoundVariables, result);
        return;
      }
      if (thatMapType.collisionsPossible(result))
        thatMapType = (MapType)that.Clone();

      foreach (TypeVariable! var in this.TypeParameters)
        thisBoundVariables.Add(var);
      foreach (TypeVariable! var in thatMapType.TypeParameters)
        thatBoundVariables.Add(var);

      try {

        for (int i = 0; i < Arguments.Length; ++i)
          Arguments[i].Unify(thatMapType.Arguments[i],
                             unifiableVariables,
                             thisBoundVariables, thatBoundVariables,
                             result);
        Result.Unify(thatMapType.Result,
                     unifiableVariables,
                     thisBoundVariables, thatBoundVariables,
                     result);

      } finally {
        // make sure that the bound variables are removed again
        for (int i = 0; i < this.TypeParameters.Length; ++i) {
          thisBoundVariables.Remove();
          thatBoundVariables.Remove();
        }
      }            
    }
#endif

    //-----------  Substitution of free variables with types not containing bound variables  -----------------

    [Pure]
    private bool collisionsPossible(IDictionary<TypeVariable!, Type!>! subst) {
      // PR: could be written more efficiently
      return exists{TypeVariable! var in TypeParameters;
                            subst.ContainsKey(var) ||
                            exists{Type! t in subst.Values; t.FreeVariables.Has(var)}};
    }

    public override Type! Substitute(IDictionary<TypeVariable!, Type!>! subst) {
      if (subst.Count == 0)
        return this;

      // there are two cases in which we have to be careful:
      // * a variable to be substituted is shadowed by a variable binder
      // * a substituted term contains variables that are bound in the
      //   type (variable capture)
      //
      // in both cases, we first clone the type to ensure that bound
      // variables are fresh

      if (collisionsPossible(subst)) {
        MapType! newType = (MapType)this.Clone();
        assert newType.Equals(this) && !newType.collisionsPossible(subst);
        return newType.Substitute(subst);
      }

      TypeSeq newArgs = new TypeSeq ();
      foreach (Type! t in Arguments)
        newArgs.Add(t.Substitute(subst));
      Type! newResult = Result.Substitute(subst);

      return new MapType(tok, TypeParameters, newArgs, newResult);
    }

    //-----------  Hashcodes  ----------------------------------

    [Pure]
    public override int GetHashCode(TypeVariableSeq! boundVariables) {
      int res = 7643761 * TypeParameters.Length + 65121 * Arguments.Length;

      foreach (TypeVariable! var in this.TypeParameters)
        boundVariables.Add(var);

      foreach (Type! t in Arguments)
        res = res * 5 + t.GetHashCode(boundVariables);
      res = res * 7 + Result.GetHashCode(boundVariables);

      for (int i = 0; i < this.TypeParameters.Length; ++i)
        boundVariables.Remove();

      return res;
    }

    //-----------  Linearisation  ----------------------------------

    public override void Emit(TokenTextWriter! stream, int contextBindingStrength) 
    {
      stream.SetToken(this);

      const int opBindingStrength = 1;
      if (opBindingStrength < contextBindingStrength)
        stream.Write("(");

      EmitOptionalTypeParams(stream, TypeParameters);

      stream.Write("[");
      Arguments.Emit(stream, ",");        // default binding strength of 0 is ok
      stream.Write("]");
      Result.Emit(stream);                 // default binding strength of 0 is ok

      if (opBindingStrength < contextBindingStrength)
        stream.Write(")");
    }

    //-----------  Resolution  ----------------------------------

    public override Type! ResolveType(ResolutionContext! rc) {
      int previousState = rc.TypeBinderState;
      try {
        foreach (TypeVariable! v in TypeParameters) {
          rc.AddTypeBinder(v);
        }

        TypeSeq resolvedArgs = new TypeSeq ();
        foreach (Type! ty in Arguments) {
          resolvedArgs.Add(ty.ResolveType(rc));
        }

        Type resolvedResult = Result.ResolveType(rc);

        CheckBoundVariableOccurrences(TypeParameters,
                                      resolvedArgs, new TypeSeq(resolvedResult),
                                      this.tok, "map arguments",
                                      rc);

        // sort the type parameters so that they are bound in the order of occurrence
        TypeVariableSeq! sortedTypeParams = SortTypeParams(TypeParameters, resolvedArgs, resolvedResult);
        return new MapType(tok, sortedTypeParams, resolvedArgs, resolvedResult);
      } finally {
        rc.TypeBinderState = previousState;
      }
    }

    public override TypeVariableSeq! FreeVariables {
      get {
        TypeVariableSeq! res = FreeVariablesIn(Arguments);
        res.AppendWithoutDups(Result.FreeVariables);
        foreach (TypeVariable! v in TypeParameters)
          res.Remove(v);
        return res;
      }
    }

    public override List<TypeProxy!>! FreeProxies { get {
      List<TypeProxy!>! res = new List<TypeProxy!> ();
      foreach (Type! t in Arguments)
        AppendWithoutDups(res, t.FreeProxies);
      AppendWithoutDups(res, Result.FreeProxies);
      return res;
    } }

    //-----------  Getters/Issers  ----------------------------------

    public override bool IsMap { get { return true; } }
    public override MapType! AsMap { get { return this; } }
    public override int MapArity { get {
      return Arguments.Length;
    } }

    //------------  Match formal argument types of the map
    //------------  on concrete types, substitute the result into the
    //------------  result type. Null is returned if so many type checking
    //------------  errors occur that the situation is hopeless

    public Type CheckArgumentTypes(ExprSeq! actualArgs,
                                   out TypeParamInstantiation! tpInstantiation,
                                   IToken! typeCheckingSubject,
                                   string! opName,
                                   TypecheckingContext! tc) {
      List<Type!>! actualTypeParams;
      TypeSeq actualResult =
        Type.CheckArgumentTypes(TypeParameters, out actualTypeParams, Arguments, actualArgs,
                                new TypeSeq (Result), null, typeCheckingSubject, opName, tc);
      if (actualResult == null) {
        tpInstantiation = SimpleTypeParamInstantiation.EMPTY;
        return null;
      } else {
        assert actualResult.Length == 1;
        tpInstantiation = SimpleTypeParamInstantiation.From(TypeParameters, actualTypeParams);
        return actualResult[0];
      }
    }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitMapType(this);
    }
  }

  //---------------------------------------------------------------------

  public enum SimpleType { Int, Bool };


  //=====================================================================

  // Interface for representing the instantiations of type parameters of
  // polymorphic functions or maps. We introduce an own interface for this
  // instead of using a simple list or dictionary, because in some cases
  // (due to the type proxies for map types) the actual number and instantiation
  // of type parameters can only be determined very late.
  public interface TypeParamInstantiation {
    // return what formal type parameters there are
    List<TypeVariable!>! FormalTypeParams { get; }
    // given a formal type parameter, return the actual instantiation
    Type! this[TypeVariable! var] { get; }
  }

  public class SimpleTypeParamInstantiation : TypeParamInstantiation {
    private readonly List<TypeVariable!>! TypeParams;
    private readonly IDictionary<TypeVariable!, Type!>! Instantiations;

    public SimpleTypeParamInstantiation(List<TypeVariable!>! typeParams,
                                        IDictionary<TypeVariable!, Type!>! instantiations) {
      this.TypeParams = typeParams;
      this.Instantiations = instantiations;
    }

    public static TypeParamInstantiation!
                  From(TypeVariableSeq! typeParams, List<Type!>! actualTypeParams)
      requires typeParams.Length == actualTypeParams.Count; {
      if (typeParams.Length == 0)
        return EMPTY;

      List<TypeVariable!>! typeParamList = new List<TypeVariable!> ();
      IDictionary<TypeVariable!, Type!>! dict = new Dictionary<TypeVariable!, Type!> ();
      for (int i = 0; i < typeParams.Length; ++i) {
        typeParamList.Add(typeParams[i]);
        dict.Add(typeParams[i], actualTypeParams[i]);
      }
      return new SimpleTypeParamInstantiation(typeParamList, dict);
    }

    public static readonly TypeParamInstantiation! EMPTY =
      new SimpleTypeParamInstantiation (new List<TypeVariable!> (),
                                        new Dictionary<TypeVariable!, Type!> ());

    // return what formal type parameters there are
    public List<TypeVariable!>! FormalTypeParams { get {
      return TypeParams;
    } }
    // given a formal type parameter, return the actual instantiation
    public Type! this[TypeVariable! var] { get {
      return Instantiations[var];
    } }
  }

  // Implementation of TypeParamInstantiation that refers to the current
  // value of a MapTypeProxy. This means that the values return by the
  // methods of this implementation can change in case the MapTypeProxy
  // receives further unifications.
  class MapTypeProxyParamInstantiation : TypeParamInstantiation {
    private readonly MapTypeProxy! Proxy;

    // the argument and result type of this particular usage of the map
    // type. these are necessary to derive the values of the type parameters
    private readonly TypeSeq! ArgumentsResult;

    // field that is initialised once all necessary information is available
    // (the MapTypeProxy is instantiated to an actual type) and the instantiation
    // of a type parameter is queried
    private IDictionary<TypeVariable!, Type!> Instantiations = null;

    public MapTypeProxyParamInstantiation(MapTypeProxy! proxy,
                                          TypeSeq! argumentsResult) {
      this.Proxy = proxy;
      this.ArgumentsResult = argumentsResult;
    }

    // return what formal type parameters there are
    public List<TypeVariable!>! FormalTypeParams { get {
      MapType realType = Proxy.ProxyFor as MapType;
      if (realType == null)
        // no instantiation of the map type is known, which means
        // that the map type is assumed to be monomorphic
        return new List<TypeVariable!> ();
      else
        return realType.TypeParameters.ToList();
    } }

    // given a formal type parameter, return the actual instantiation
    public Type! this[TypeVariable! var] { get {
      // then there has to be an instantiation that is a polymorphic map type
      if (Instantiations == null) {
        MapType realType = Proxy.ProxyFor as MapType;
        assert realType != null;
        TypeSeq! formalArgs = new TypeSeq ();
        foreach (Type! t in realType.Arguments)
          formalArgs.Add(t);
        formalArgs.Add(realType.Result);
        Instantiations =
          Type.InferTypeParameters(realType.TypeParameters, formalArgs, ArgumentsResult);
      }
      return Instantiations[var];
    } }
  }

}