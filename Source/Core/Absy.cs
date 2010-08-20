//-----------------------------------------------------------------------------
//
// Copyright (C) Microsoft Corporation.  All Rights Reserved.
//
//-----------------------------------------------------------------------------
//---------------------------------------------------------------------------------------------
// BoogiePL - Absy.cs
//---------------------------------------------------------------------------------------------
namespace Microsoft.Boogie.AbstractInterpretation
{
  using System.Diagnostics;
  using CCI = System.Compiler;
  using System.Collections;
  using AI = Microsoft.AbstractInterpretationFramework;

  public class CallSite
  {
    public readonly Implementation! Impl;
    public readonly Block! Block;
    public readonly int Statement; // invariant: Block[Statement] is CallCmd
    public readonly AI.Lattice.Element! KnownBeforeCall;
    public readonly ProcedureSummaryEntry! SummaryEntry;

    public CallSite (Implementation! impl, Block! b, int stmt, AI.Lattice.Element! e, ProcedureSummaryEntry! summaryEntry)
    {
      this.Impl = impl;
      this.Block = b;
      this.Statement = stmt;
      this.KnownBeforeCall = e;
      this.SummaryEntry = summaryEntry;
    }
  }

  public class ProcedureSummaryEntry
  {
    public AI.Lattice! Lattice;
    public AI.Lattice.Element! OnEntry;
    public AI.Lattice.Element! OnExit;
    public CCI.IMutableSet/*<CallSite>*/! ReturnPoints; // whenever OnExit changes, we start analysis again at all the ReturnPoints

    public ProcedureSummaryEntry (AI.Lattice! lattice, AI.Lattice.Element! onEntry)
    {
      this.Lattice = lattice;
      this.OnEntry = onEntry;
      this.OnExit = lattice.Bottom;
      this.ReturnPoints = new CCI.HashSet();
      // base();
    }

  } // class

  public class ProcedureSummary : ArrayList/*<ProcedureSummaryEntry>*/
  {
    invariant !IsReadOnly && !IsFixedSize;

    public new ProcedureSummaryEntry! this [int i]
    {
      get
        requires 0 <= i && i < Count;
      { return (ProcedureSummaryEntry!) base[i]; }
    }

  } // class
} // namespace


namespace Microsoft.Boogie
{
  using System;
  using System.Collections;
  using System.Diagnostics;
  using System.Collections.Generic;
  using Microsoft.Boogie.AbstractInterpretation;
  using AI = Microsoft.AbstractInterpretationFramework;
  using Microsoft.Contracts;
  using Graphing;


  public abstract class Absy
  {
    public IToken! tok;
    private int uniqueId;

    public int Line { get { return tok != null ? tok.line : -1; } }
    public int Col  { get { return tok != null ? tok.col : -1; } }

    public Absy (IToken! tok)
    {
      this.tok = tok;
      this.uniqueId = AbsyNodeCount++;
      // base();
    }

    private static int AbsyNodeCount = 0;

    // We uniquely number every AST node to make them
    // suitable for our implementation of functional maps.
    //
    public int UniqueId { get { return this.uniqueId; } }

    private const int indent_size = 2;
    protected static string Indent (int level)
    {
      return new string(' ', (indent_size * level));
    }

    public abstract void Resolve (ResolutionContext! rc);

    /// <summary>
    /// Requires the object to have been successfully resolved.
    /// </summary>
    /// <param name="tc"></param>
    public abstract void Typecheck (TypecheckingContext! tc);
    /// <summary>
    /// Intorduced this so the uniqueId is not the same on a cloned object.
    /// </summary>
    /// <param name="tc"></param>
    public virtual Absy! Clone()
    {
      Absy! result = (Absy!) this.MemberwiseClone();
      result.uniqueId = AbsyNodeCount++; // BUGBUG??
      return result;
    }

    public virtual Absy! StdDispatch(StandardVisitor! visitor)
    {
      System.Diagnostics.Debug.Fail("Unknown Absy node type: " + this.GetType());
      throw new System.NotImplementedException();
    }
  }

  // TODO: Ideally, this would use generics.
  public interface IPotentialErrorNode
  {
    object ErrorData { get; set; }
  }

  public class Program : Absy
  {
    [Rep]
    public List<Declaration!>! TopLevelDeclarations;

    public Program()
      : base(Token.NoToken)
    {
      this.TopLevelDeclarations = new List<Declaration!>();
      // base(Token.NoToken);
    }

    public void Emit (TokenTextWriter! stream)
    {
      stream.SetToken(this);
      Emitter.Declarations(this.TopLevelDeclarations, stream);
    }
    /// <summary>
    /// Returns the number of name resolution errors.
    /// </summary>
    /// <returns></returns>
    public int Resolve ()
    {
        return Resolve((IErrorSink) null);
    }

    public int Resolve (IErrorSink errorSink)
    {
      ResolutionContext rc = new ResolutionContext(errorSink);
      Resolve(rc);
      return rc.ErrorCount;
    }

    public override void Resolve (ResolutionContext! rc)
    {
      Helpers.ExtraTraceInformation("Starting resolution");
      
      foreach (Declaration d in TopLevelDeclarations) {
        d.Register(rc);
      }

      ResolveTypes(rc);

      List<Declaration!> prunedTopLevelDecls = CommandLineOptions.Clo.OverlookBoogieTypeErrors ? new List<Declaration!>() : null;

      foreach (Declaration d in TopLevelDeclarations) {
        // resolve all the non-type-declarations
        if (d is TypeCtorDecl || d is TypeSynonymDecl) {
          if (prunedTopLevelDecls != null)
            prunedTopLevelDecls.Add(d);
        } else {
          int e = rc.ErrorCount;
          d.Resolve(rc);
          if (prunedTopLevelDecls != null) {
            if (rc.ErrorCount != e && d is Implementation) {
              // ignore this implementation
              System.Console.WriteLine("Warning: Ignoring implementation {0} because of translation resolution errors", ((Implementation)d).Name);
              rc.ErrorCount = e;
            } else {
              prunedTopLevelDecls.Add(d);
            }
          }
        }
      }
      if (prunedTopLevelDecls != null) {
        TopLevelDeclarations = prunedTopLevelDecls;
      }

      foreach (Declaration d in TopLevelDeclarations) {
        Variable v = d as Variable;
        if (v != null) {
          v.ResolveWhere(rc);
        }
      }
    }


    private void ResolveTypes (ResolutionContext! rc) {
      // first resolve type constructors
      foreach (Declaration d in TopLevelDeclarations) {
        if (d is TypeCtorDecl)
          d.Resolve(rc);
      }

      // collect type synonym declarations
      List<TypeSynonymDecl!>! synonymDecls = new List<TypeSynonymDecl!> ();
      foreach (Declaration d in TopLevelDeclarations) {
        if (d is TypeSynonymDecl)
          synonymDecls.Add((TypeSynonymDecl)d);
      }

      // then resolve the type synonyms by a simple
      // fixed-point iteration
      TypeSynonymDecl.ResolveTypeSynonyms(synonymDecls, rc);
    }
    

    public int Typecheck ()
    {
      return this.Typecheck((IErrorSink) null);
    }

    public int Typecheck (IErrorSink errorSink)
    {
      TypecheckingContext tc = new TypecheckingContext(errorSink);
      Typecheck(tc);
      return tc.ErrorCount;
    }

    public override void Typecheck (TypecheckingContext! tc)
    {
      Helpers.ExtraTraceInformation("Starting typechecking");

      int oldErrorCount = tc.ErrorCount;
      foreach (Declaration d in TopLevelDeclarations) {
        d.Typecheck(tc);
      }

      if (oldErrorCount == tc.ErrorCount) {
        // check whether any type proxies have remained uninstantiated
        TypeAmbiguitySeeker! seeker = new TypeAmbiguitySeeker (tc);
        foreach (Declaration d in TopLevelDeclarations) {
          seeker.Visit(d);
        }
      }

      AxiomExpander expander = new AxiomExpander(this, tc);
      expander.CollectExpansions();
    }

    public void ComputeStronglyConnectedComponents()
    {
      foreach(Declaration d in this.TopLevelDeclarations) {
        d.ComputeStronglyConnectedComponents();
      }
    }

    public void InstrumentWithInvariants ()
    {
      foreach (Declaration d in this.TopLevelDeclarations) {
        d.InstrumentWithInvariants();
      }
    }

    /// <summary>
    /// Reset the abstract stated computed before
    /// </summary>
    public void ResetAbstractInterpretationState()
    {
      foreach(Declaration d in this.TopLevelDeclarations) {
        d.ResetAbstractInterpretationState();
      }
    }

    public void UnrollLoops(int n)
      requires 0 <= n;
    {
      foreach (Declaration d in this.TopLevelDeclarations) {
        Implementation impl = d as Implementation;
        if (impl != null && impl.Blocks != null && impl.Blocks.Count > 0) {
          expose (impl) {
            Block start = impl.Blocks[0];
            assume start != null;
            assume start.IsConsistent;
            impl.Blocks = LoopUnroll.UnrollLoops(start, n);
          }
        }
      }
    }

    void CreateProceduresForLoops(Implementation! impl, Graph<Block!>! g, List<Implementation!>! loopImpls)
    {
        // Enumerate the headers 
        // for each header h:
        //   create implementation p_h with 
        //     inputs = inputs, outputs, and locals of impl
        //     outputs = outputs and locals of impl
        //     locals = empty set
        //   add call o := p_h(i) at the beginning of the header block
        //   break the back edges whose target is h
        // Enumerate the headers again to create the bodies of p_h 
        // for each header h:
        //   compute the loop corresponding to h
        //   make copies of all blocks in the loop for h
        //   delete all target edges that do not go to a block in the loop
        //   create a new entry block and a new return block
        //   add edges from entry block to the loop header and the return block
        //   add calls o := p_h(i) at the end of the blocks that are sources of back edges
        Dictionary<Block!, string!>! loopHeaderToName = new Dictionary<Block!, string!>();
        Dictionary<Block!, VariableSeq!>! loopHeaderToInputs = new Dictionary<Block!, VariableSeq!>();
        Dictionary<Block!, VariableSeq!>! loopHeaderToOutputs = new Dictionary<Block!, VariableSeq!>();
        Dictionary<Block!, Hashtable!>! loopHeaderToSubstMap = new Dictionary<Block!, Hashtable!>();
        Dictionary<Block!, Procedure!>! loopHeaderToLoopProc = new Dictionary<Block!, Procedure!>();
        Dictionary<Block!, CallCmd!>! loopHeaderToCallCmd = new Dictionary<Block!, CallCmd!>();
        foreach (Block! header in g.Headers)
        {
            Contract.Assert(header != null);
            string name = header.ToString();
            loopHeaderToName[header] = name;
            VariableSeq inputs = new VariableSeq();
            VariableSeq outputs = new VariableSeq();
            ExprSeq callInputs = new ExprSeq();
            IdentifierExprSeq callOutputs = new IdentifierExprSeq();
            Hashtable substMap = new Hashtable(); // Variable -> IdentifierExpr

            foreach (Variable! v in impl.InParams)
            {
                callInputs.Add(new IdentifierExpr(Token.NoToken, v));
                Formal f = new Formal(Token.NoToken, new TypedIdent(Token.NoToken, "in_" + v.Name, v.TypedIdent.Type), true);
                inputs.Add(f);
                substMap[v] = new IdentifierExpr(Token.NoToken, f);
            }
            foreach (Variable! v in impl.OutParams)
            {
                callInputs.Add(new IdentifierExpr(Token.NoToken, v));
                inputs.Add(new Formal(Token.NoToken, new TypedIdent(Token.NoToken, "in_" + v.Name, v.TypedIdent.Type), true));
                callOutputs.Add(new IdentifierExpr(Token.NoToken, v));
                Formal f = new Formal(Token.NoToken, new TypedIdent(Token.NoToken, "out_" + v.Name, v.TypedIdent.Type), false);
                outputs.Add(f);
                substMap[v] = new IdentifierExpr(Token.NoToken, f);
            }
            foreach (Variable! v in impl.LocVars)
            {
                callInputs.Add(new IdentifierExpr(Token.NoToken, v));
                inputs.Add(new Formal(Token.NoToken, new TypedIdent(Token.NoToken, "in_" + v.Name, v.TypedIdent.Type), true));
                callOutputs.Add(new IdentifierExpr(Token.NoToken, v));
                Formal f = new Formal(Token.NoToken, new TypedIdent(Token.NoToken, "out_" + v.Name, v.TypedIdent.Type), false);
                outputs.Add(f);
                substMap[v] = new IdentifierExpr(Token.NoToken, f);
            }
            VariableSeq! targets = new VariableSeq();
            foreach (Block! b in g.BackEdgeNodes(header))
            {
              foreach (Block! block in g.NaturalLoops(header, b))
              {
                foreach (Cmd! cmd in block.Cmds)
                {
                  cmd.AddAssignedVariables(targets);
                }
              }
            }
            IdentifierExprSeq! globalMods = new IdentifierExprSeq();
            Set globalModsSet = new Set();
            foreach (Variable! v in targets)
            {
              if (!(v is GlobalVariable)) continue;
              if (globalModsSet.Contains(v)) continue;
              globalModsSet.Add(v);
              globalMods.Add(new IdentifierExpr(Token.NoToken, v));
            }
            loopHeaderToInputs[header] = inputs;
            loopHeaderToOutputs[header] = outputs;
            loopHeaderToSubstMap[header] = substMap;
            Procedure! proc = 
                new Procedure(Token.NoToken, "loop_" + header.ToString(),
                              new TypeVariableSeq(), inputs, outputs,
                              new RequiresSeq(), globalMods, new EnsuresSeq());
            if (CommandLineOptions.Clo.LazyInlining > 0 || CommandLineOptions.Clo.StratifiedInlining > 0)
            {
              proc.AddAttribute("inline", Expr.Literal(1));
            }
            loopHeaderToLoopProc[header] = proc;
            CallCmd callCmd = new CallCmd(Token.NoToken, name, callInputs, callOutputs);
            callCmd.Proc = proc;
            loopHeaderToCallCmd[header] = callCmd;
        }

        foreach (Block! header in g.Headers)
        {
            Procedure loopProc = loopHeaderToLoopProc[header];
            Dictionary<Block, Block> blockMap = new Dictionary<Block, Block>();
            CodeCopier codeCopier = new CodeCopier(loopHeaderToSubstMap[header]);  // fix me
            VariableSeq inputs = loopHeaderToInputs[header];
            VariableSeq outputs = loopHeaderToOutputs[header];
            foreach (Block! source in g.BackEdgeNodes(header))
            {
                foreach (Block! block in g.NaturalLoops(header, source))
                {
                    if (blockMap.ContainsKey(block)) continue;
                    Block newBlock = new Block();
                    newBlock.Label = block.Label;
                    newBlock.Cmds = codeCopier.CopyCmdSeq(block.Cmds);
                    blockMap[block] = newBlock;
                }
                string callee = loopHeaderToName[header];
                ExprSeq ins = new ExprSeq();
                IdentifierExprSeq outs = new IdentifierExprSeq();
                for (int i = 0; i < impl.InParams.Length; i++)
                {
                  ins.Add(new IdentifierExpr(Token.NoToken, (!) inputs[i]));
                }
                foreach (Variable! v in outputs)
                {
                    ins.Add(new IdentifierExpr(Token.NoToken, v));
                    outs.Add(new IdentifierExpr(Token.NoToken, v));
                }
                CallCmd callCmd = new CallCmd(Token.NoToken, callee, ins, outs);
                callCmd.Proc = loopProc;
                Block! block1 = new Block(Token.NoToken, source.Label + "_dummy",
                                    new CmdSeq(new AssumeCmd(Token.NoToken, Expr.False)), new ReturnCmd(Token.NoToken));
                Block! block2 = new Block(Token.NoToken, block1.Label, 
                                    new CmdSeq(callCmd), new ReturnCmd(Token.NoToken));
                impl.Blocks.Add(block1);
                
                GotoCmd gotoCmd = source.TransferCmd as GotoCmd;
                assert gotoCmd != null && gotoCmd.labelNames != null && gotoCmd.labelTargets != null && gotoCmd.labelTargets.Length >= 1;
                StringSeq! newLabels = new StringSeq();
                BlockSeq! newTargets = new BlockSeq();
                for (int i = 0; i < gotoCmd.labelTargets.Length; i++)
                {
                  if (gotoCmd.labelTargets[i] == header) continue;
                  newTargets.Add(gotoCmd.labelTargets[i]);
                  newLabels.Add(gotoCmd.labelNames[i]);
                }
                newTargets.Add(block1);
                newLabels.Add(block1.Label);
                gotoCmd.labelNames = newLabels;
                gotoCmd.labelTargets = newTargets;
                
                blockMap[block1] = block2;
            }   
            List<Block!>! blocks = new List<Block!>();
            Block exit = new Block(Token.NoToken, "exit", new CmdSeq(), new ReturnCmd(Token.NoToken));
            GotoCmd cmd = new GotoCmd(Token.NoToken,
                                        new StringSeq(((!)blockMap[header]).Label, exit.Label),
                                        new BlockSeq(blockMap[header], exit));
            
            Debug.Assert(outputs.Length + impl.InParams.Length == inputs.Length);
            List<AssignLhs!>! lhss = new List<AssignLhs!>();
            List<Expr!>! rhss = new List<Expr!>();
            for (int i = impl.InParams.Length; i < inputs.Length; i++)
            {
                Variable! inv = (!)inputs[i];
                Variable! outv = (!)outputs[i - impl.InParams.Length];
                AssignLhs lhs = new SimpleAssignLhs(Token.NoToken, new IdentifierExpr(Token.NoToken, outv));
                Expr rhs = new IdentifierExpr(Token.NoToken, inv);
                lhss.Add(lhs);
                rhss.Add(rhs);
            }
            AssignCmd assignCmd = new AssignCmd(Token.NoToken, lhss, rhss);
            Block entry = new Block(Token.NoToken, "entry", new CmdSeq(assignCmd), cmd);
            blocks.Add(entry); 
            foreach (Block! block in blockMap.Keys)
            {
                Block! newBlock = (!) blockMap[block];
                GotoCmd gotoCmd = block.TransferCmd as GotoCmd;
                if (gotoCmd == null)
                {
                    newBlock.TransferCmd = new ReturnCmd(Token.NoToken);
                }
                else
                {
                    assume gotoCmd.labelNames != null && gotoCmd.labelTargets != null;
                    StringSeq newLabels = new StringSeq();
                    BlockSeq newTargets = new BlockSeq();
                    for (int i = 0; i < gotoCmd.labelTargets.Length; i++)
                    {
                        Block target = gotoCmd.labelTargets[i];
                        if (blockMap.ContainsKey(target))
                        {
                            newLabels.Add(gotoCmd.labelNames[i]);
                            newTargets.Add(blockMap[target]);
                        }
                    }
                    if (newTargets.Length == 0)
                    {
                        newBlock.Cmds.Add(new AssumeCmd(Token.NoToken, Expr.False));
                        newBlock.TransferCmd = new ReturnCmd(Token.NoToken);
                    }
                    else
                    {
                        newBlock.TransferCmd = new GotoCmd(Token.NoToken, newLabels, newTargets);
                    }
                }
                blocks.Add(newBlock);
            }
            blocks.Add(exit);
            Implementation loopImpl = 
                new Implementation(Token.NoToken, loopProc.Name,
                                    new TypeVariableSeq(), inputs, outputs, new VariableSeq(), blocks);
            loopImpl.Proc = loopProc;
            loopImpls.Add(loopImpl);
            
            // Finally, add call to the loop in the containing procedure
            CmdSeq cmdSeq = new CmdSeq();
            cmdSeq.Add(loopHeaderToCallCmd[header]);
            cmdSeq.AddRange(header.Cmds);
            header.Cmds = cmdSeq;
        }
    }
    
    public static Graph<Block!>! GraphFromImpl(Implementation! impl) {
      Contract.Ensures(Contract.Result<Graph<Block>>() != null);

      Graph<Block!> g = new Graph<Block!>();
      g.AddSource(impl.Blocks[0]); // there is always at least one node in the graph
      foreach (Block b in impl.Blocks) {
        Contract.Assert(b != null);
        GotoCmd gtc = b.TransferCmd as GotoCmd;
        if (gtc != null) {
          foreach (Block! dest in (!)gtc.labelTargets) {
            g.AddEdge(b, dest);
          }
        }
      }
      return g;
    }
    
    public void ExtractLoops()
    {
      List<Implementation!>! loopImpls = new List<Implementation!>();
      foreach (Declaration d in this.TopLevelDeclarations) {
        Implementation impl = d as Implementation;
        if (impl != null && impl.Blocks != null && impl.Blocks.Count > 0) {
            Graph<Block!>! g = GraphFromImpl(impl);
            g.ComputeLoops();
            if (!g.Reducible)
            {
              throw new Exception("Irreducible flow graphs are unsupported.");
            }
            CreateProceduresForLoops(impl, g, loopImpls);
        }
      }
      foreach (Implementation! loopImpl in loopImpls)
      {
        TopLevelDeclarations.Add(loopImpl);
      }
    }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitProgram(this);
    }
    
    private List<GlobalVariable!> globals = null;
    public List<GlobalVariable!>! GlobalVariables()
    {
      if (globals != null) return globals;
      globals = new List<GlobalVariable!>();
      foreach (Declaration d in TopLevelDeclarations) {
        GlobalVariable gvar = d as GlobalVariable;
        if (gvar != null) globals.Add(gvar);
      }
      return globals;
    }
  }

  //---------------------------------------------------------------------
  // Declarations

  public abstract class Declaration : Absy
  {
    public QKeyValue Attributes;

    public Declaration(IToken! tok)
      : base(tok)
    {
    }

    protected void EmitAttributes(TokenTextWriter! stream)
    {
      for (QKeyValue kv = this.Attributes; kv != null; kv = kv.Next) {
        kv.Emit(stream);
        stream.Write(" ");
      }
    }

    protected void ResolveAttributes(ResolutionContext! rc)
    {
      for (QKeyValue kv = this.Attributes; kv != null; kv = kv.Next) {
        kv.Resolve(rc);
      }
    }

    protected void TypecheckAttributes(TypecheckingContext! rc)
    {
      for (QKeyValue kv = this.Attributes; kv != null; kv = kv.Next) {
        kv.Typecheck(rc);
      }
    }

    // Look for {:name true} or {:name false} in list of attributes. Return result in 'result'
    // (which is not touched if there is no attribute specified).
    //
    // Returns false is there was an error processing the flag, true otherwise.
    public bool CheckBooleanAttribute(string! name, ref bool result)
    {
      Expr? expr = FindExprAttribute(name);
      if (expr != null) {
        if (expr is LiteralExpr && ((LiteralExpr)expr).isBool) {
          result = ((LiteralExpr)expr).asBool;
        } else {
          return false;
        }
      }
      return true;
    }

    // Look for {:name expr} in list of attributes.
    public Expr? FindExprAttribute(string! name)
    {
      Expr? res = null;
      for (QKeyValue kv = this.Attributes; kv != null; kv = kv.Next) {
        if (kv.Key == name) {
          if (kv.Params.Count == 1 && kv.Params[0] is Expr) {
            res = (Expr)kv.Params[0];
          }
        }
      }
      return res;
    }

    // Look for {:name string} in list of attributes.
    public string? FindStringAttribute(string! name)
    {
      return QKeyValue.FindStringAttribute(this.Attributes, name);
    }

    // Look for {:name N} or {:name N} in list of attributes. Return result in 'result'
    // (which is not touched if there is no attribute specified).
    //
    // Returns false is there was an error processing the flag, true otherwise.
    public bool CheckIntAttribute(string! name, ref int result)
    {
      Expr? expr = FindExprAttribute(name);
      if (expr != null) {
        if (expr is LiteralExpr && ((LiteralExpr)expr).isBigNum) {
          result = ((LiteralExpr)expr).asBigNum.ToInt;
        } else {
          return false;
        }
      }
      return true;
    }

    public void AddAttribute(string! name, object! val)
    {
      QKeyValue kv;
      for (kv = this.Attributes; kv != null; kv = kv.Next) {
        if (kv.Key == name) {
          kv.Params.Add(val);
          break;
        }
      }
      if (kv == null) {
        Attributes = new QKeyValue(tok, name, new List<object!>(new object![] { val }), Attributes);
      }
    }                                   
    
    public abstract void Emit(TokenTextWriter! stream, int level);
    public abstract void Register(ResolutionContext! rc);

    /// <summary>
    /// Compute the strongly connected components of the declaration.
    /// By default, it does nothing
    /// </summary>
    public virtual void ComputeStronglyConnectedComponents() { /* Does nothing */}

    /// <summary>
    /// This method inserts the abstract-interpretation-inferred invariants
    /// as assume (or possibly assert) statements in the statement sequences of
    /// each block.
    /// </summary>
    public virtual void InstrumentWithInvariants () {}

    /// <summary>
    /// Reset the abstract stated computed before
    /// </summary>
    public virtual void ResetAbstractInterpretationState() { /* does nothing */    }
  }

  public class Axiom : Declaration
  {
    public Expr! Expr;
    public string? Comment;

    public Axiom(IToken! tok, Expr! expr)
    {
      this(tok, expr, null);
    }

    public Axiom(IToken! tok, Expr! expr, string? comment)
      : base(tok)
    {
      Expr = expr;
      Comment = comment;
      // base(tok);
    }

    public Axiom(IToken! tok, Expr! expr, string? comment, QKeyValue kv)
    {
      this(tok, expr, comment);
      this.Attributes = kv;
    }

    public override void Emit(TokenTextWriter! stream, int level)
    {
      if (Comment != null) {
        stream.WriteLine(this, level, "// " + Comment);
      }
      stream.Write(this, level, "axiom ");
      EmitAttributes(stream);
      this.Expr.Emit(stream);
      stream.WriteLine(";");
    }
    public override void Register(ResolutionContext! rc)
    {
      // nothing to register
    }
    public override void Resolve(ResolutionContext! rc)
    {
      ResolveAttributes(rc);
      rc.StateMode = ResolutionContext.State.StateLess;
      Expr.Resolve(rc);
      rc.StateMode = ResolutionContext.State.Single;
    }
    public override void Typecheck(TypecheckingContext! tc)
    {
      TypecheckAttributes(tc);
      Expr.Typecheck(tc);
      assert Expr.Type != null;  // follows from postcondition of Expr.Typecheck
      if (! Expr.Type.Unify(Type.Bool)) 
      {
        tc.Error(this, "axioms must be of type bool");
      }
    }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitAxiom(this);
    }
  }

  public abstract class NamedDeclaration : Declaration
  {
    private string! name;
    public string! Name
    {
      get
      {
        return this.name;
      }
      set
      {
        this.name = value;
      }
    }


    public NamedDeclaration(IToken! tok, string! name)
      : base(tok)
    {
      this.name = name;
      // base(tok);
    }
    [Pure]
    public override string! ToString()
    {
      return (!) Name;
    }
  }  

  public class TypeCtorDecl : NamedDeclaration
  {
    public readonly int Arity;

	public TypeCtorDecl(IToken! tok, string! name, int Arity)
      : base(tok, name)
    {
        this.Arity = Arity;
    }
    public TypeCtorDecl(IToken! tok, string! name, int Arity, QKeyValue kv)
      : base(tok, name)
    {
        this.Arity = Arity;
        this.Attributes = kv;
    }
    public override void Emit(TokenTextWriter! stream, int level)
    {
      stream.Write(this, level, "type ");
      EmitAttributes(stream);
      stream.Write("{0}", TokenTextWriter.SanitizeIdentifier(Name));
      for (int i = 0; i < Arity; ++i)
        stream.Write(" _");
      stream.WriteLine(";");
    }
    public override void Register(ResolutionContext! rc)
    {
      rc.AddType(this);
    }
    public override void Resolve(ResolutionContext! rc)
    {
      ResolveAttributes(rc);
    }
    public override void Typecheck(TypecheckingContext! tc)
    {
      TypecheckAttributes(tc);
    }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitTypeCtorDecl(this);
    }
  }


  public class TypeSynonymDecl : NamedDeclaration
  {
    public TypeVariableSeq! TypeParameters;
    public Type! Body;

    public TypeSynonymDecl(IToken! tok, string! name,
                           TypeVariableSeq! typeParams, Type! body)
      : base(tok, name)
    {
      this.TypeParameters = typeParams;
      this.Body = body;
    }
    public TypeSynonymDecl(IToken! tok, string! name,
                           TypeVariableSeq! typeParams, Type! body, QKeyValue kv)
      : base(tok, name)
    {
      this.TypeParameters = typeParams;
      this.Body = body;
      this.Attributes = kv;
    }
    public override void Emit(TokenTextWriter! stream, int level)
    {
      stream.Write(this, level, "type ");
      EmitAttributes(stream);
      stream.Write("{0}", TokenTextWriter.SanitizeIdentifier(Name));
      if (TypeParameters.Length > 0)
        stream.Write(" ");
      TypeParameters.Emit(stream, " ");
      stream.Write(" = ");
      Body.Emit(stream);
      stream.WriteLine(";");
    }
    public override void Register(ResolutionContext! rc)
    {
      rc.AddType(this);
    }
    public override void Resolve(ResolutionContext! rc) 
    {
      ResolveAttributes(rc);

      int previousState = rc.TypeBinderState;
      try {
        foreach (TypeVariable! v in TypeParameters)
          rc.AddTypeBinder(v);
        Body = Body.ResolveType(rc);
      } finally {
        rc.TypeBinderState = previousState;
      }
    }
    public override void Typecheck(TypecheckingContext! tc) 
    {
      TypecheckAttributes(tc);
    }

    public static void ResolveTypeSynonyms(List<TypeSynonymDecl!>! synonymDecls,
                                           ResolutionContext! rc) {
      // then discover all dependencies between type synonyms
      IDictionary<TypeSynonymDecl!, List<TypeSynonymDecl!>!>! deps =
        new Dictionary<TypeSynonymDecl!, List<TypeSynonymDecl!>!> ();
      foreach (TypeSynonymDecl! decl in synonymDecls) {
        List<TypeSynonymDecl!>! declDeps = new List<TypeSynonymDecl!> ();
        FindDependencies(decl.Body, declDeps, rc);
        deps.Add(decl, declDeps);
      }

      List<TypeSynonymDecl!>! resolved = new List<TypeSynonymDecl!> ();

      int unresolved = synonymDecls.Count - resolved.Count;
      while (unresolved > 0) {
        foreach (TypeSynonymDecl! decl in synonymDecls) {
          if (!resolved.Contains(decl) &&
              forall{TypeSynonymDecl! d in deps[decl]; resolved.Contains(d)}) {
            decl.Resolve(rc);
            resolved.Add(decl);
          }
        }

        int newUnresolved = synonymDecls.Count - resolved.Count;
        if (newUnresolved < unresolved) {
          // we are making progress
          unresolved = newUnresolved;
        } else {
          // there have to be cycles in the definitions
          foreach (TypeSynonymDecl! decl in synonymDecls) {
            if (!resolved.Contains(decl)) {
	          rc.Error(decl,
                       "type synonym could not be resolved because of cycles: {0}" +
                       " (replacing body with \"bool\" to continue resolving)",
                       decl.Name);

              // we simply replace the bodies of all remaining type
              // synonyms with "bool" so that resolution can continue
              decl.Body = Type.Bool;
              decl.Resolve(rc);
            }
          }

          unresolved = 0;
        }
      }
    }

    // determine a list of all type synonyms that occur in "type"
    private static void FindDependencies(Type! type, List<TypeSynonymDecl!>! deps,
                                         ResolutionContext! rc) {
      if (type.IsVariable || type.IsBasic) {
        // nothing
      } else if (type.IsUnresolved) {
        UnresolvedTypeIdentifier! unresType = type.AsUnresolved;
        TypeSynonymDecl dep = rc.LookUpTypeSynonym(unresType.Name);
        if (dep != null)
          deps.Add(dep);
        foreach (Type! subtype in unresType.Arguments)
          FindDependencies(subtype, deps, rc);
      } else if (type.IsMap) {
        MapType! mapType = type.AsMap;
        foreach (Type! subtype in mapType.Arguments)
          FindDependencies(subtype, deps, rc);
        FindDependencies(mapType.Result, deps, rc);
      } else if (type.IsCtor) {
        // this can happen because we allow types to be resolved multiple times
        CtorType! ctorType = type.AsCtor;
        foreach (Type! subtype in ctorType.Arguments)
          FindDependencies(subtype, deps, rc);
      } else {
        System.Diagnostics.Debug.Fail("Did not expect this type during resolution: "
                                      + type);
      }
    }
   

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitTypeSynonymDecl(this);
    }
  }


  public abstract class Variable : NamedDeclaration, AI.IVariable
  {
    public TypedIdent! TypedIdent;
    public Variable(IToken! tok, TypedIdent! typedIdent)
      : base(tok, typedIdent.Name)
    {
      this.TypedIdent = typedIdent;
      // base(tok, typedIdent.Name);
    }

    public Variable(IToken! tok, TypedIdent! typedIdent, QKeyValue kv)
      : base(tok, typedIdent.Name)
    {
      this.TypedIdent = typedIdent;
      // base(tok, typedIdent.Name);
      this.Attributes = kv;
    }

    public abstract bool IsMutable
    {
      get;
    }

    public override void Emit(TokenTextWriter! stream, int level)
    {
      stream.Write(this, level, "var ");
      EmitAttributes(stream);
      EmitVitals(stream, level);
      stream.WriteLine(";");
    }
    public void EmitVitals(TokenTextWriter! stream, int level)
    {
      if (CommandLineOptions.Clo.PrintWithUniqueASTIds && this.TypedIdent.HasName)
      {
        stream.Write("h{0}^^", this.GetHashCode());  // the idea is that this will prepend the name printed by TypedIdent.Emit
      }
      this.TypedIdent.Emit(stream);
    }
    public override void Resolve(ResolutionContext! rc)
    {
      this.TypedIdent.Resolve(rc);
    }
    public void ResolveWhere(ResolutionContext! rc)
    {
      if (this.TypedIdent.WhereExpr != null) {
        this.TypedIdent.WhereExpr.Resolve(rc);
      }
      ResolveAttributes(rc);
    }
    public override void Typecheck(TypecheckingContext! tc)
    {
      TypecheckAttributes(tc);
      this.TypedIdent.Typecheck(tc);
    }
    [Pure]
    public object DoVisit(AI.ExprVisitor! visitor)
    {
      return visitor.VisitVariable(this);
    }
  }

  public class VariableComparer : IComparer
  {
    public int Compare(object a, object b) {
        Variable A = a as Variable;
        Variable B = b as Variable;
        if (A == null || B == null) {
            throw new ArgumentException("VariableComparer works only on objects of type Variable");
        }
        return ((!)A.Name).CompareTo(B.Name);
    }
  }

  // class to specify the <:-parents of the values of constants
  public class ConstantParent {
    public readonly IdentifierExpr! Parent;
    // if true, the sub-dag underneath this constant-parent edge is
    // disjoint from all other unique sub-dags
    public readonly bool Unique;

    public ConstantParent(IdentifierExpr! parent, bool unique) {
      Parent = parent;
      Unique = unique;
    }
  }

  public class Constant : Variable 
  {
    // when true, the value of this constant is meant to be distinct
    // from all other constants.
    public readonly bool Unique;

    // the <:-parents of the value of this constant. If the field is
    // null, no information about the parents is provided, which means
    // that the parental situation is unconstrained.
    public readonly List<ConstantParent!> Parents;

    // if true, it is assumed that the immediate <:-children of the
    // value of this constant are completely specified
    public readonly bool ChildrenComplete;

    public Constant(IToken! tok, TypedIdent! typedIdent)
      : base(tok, typedIdent)
      requires typedIdent.Name != null && typedIdent.Name.Length > 0;
      requires typedIdent.WhereExpr == null;
    {
      // base(tok, typedIdent);
      this.Unique = true;
      this.Parents = null;
      this.ChildrenComplete = false;
    }
    public Constant(IToken! tok, TypedIdent! typedIdent, bool unique)
      : base(tok, typedIdent)
      requires typedIdent.Name != null && typedIdent.Name.Length > 0;
      requires typedIdent.WhereExpr == null;
    {
      // base(tok, typedIdent);
      this.Unique = unique;
      this.Parents = null;
      this.ChildrenComplete = false;
    }
    public Constant(IToken! tok, TypedIdent! typedIdent,
                    bool unique,
                    List<ConstantParent!> parents, bool childrenComplete,
                    QKeyValue kv)
      : base(tok, typedIdent, kv)
      requires typedIdent.Name != null && typedIdent.Name.Length > 0;
      requires typedIdent.WhereExpr == null;
    {
      // base(tok, typedIdent);
      this.Unique = unique;
      this.Parents = parents;
      this.ChildrenComplete = childrenComplete;
    }
    public override bool IsMutable 
    {
      get
      {
        return false;
      }
    }
    public override void Emit(TokenTextWriter! stream, int level)
    {
      stream.Write(this, level, "const ");
      EmitAttributes(stream);
      if (this.Unique){
        stream.Write(this, level, "unique ");
      }
      EmitVitals(stream, level);

      if (Parents != null || ChildrenComplete) {
        stream.Write(this, level, " extends");
        string! sep = " ";
        foreach (ConstantParent! p in (!)Parents) {
          stream.Write(this, level, sep);
          sep = ", ";
          if (p.Unique)
            stream.Write(this, level, "unique ");
          p.Parent.Emit(stream);
        }
        if (ChildrenComplete)
          stream.Write(this, level, " complete");
      }
      
      stream.WriteLine(";");
    }
    public override void Register(ResolutionContext! rc)
    {
      rc.AddVariable(this, true);
    }
    public override void Resolve(ResolutionContext! rc) 
    {
      base.Resolve(rc);
      if (Parents != null) {
        foreach (ConstantParent! p in Parents) {
          p.Parent.Resolve(rc);
          if (p.Parent.Decl != null && !(p.Parent.Decl is Constant))
            rc.Error(p.Parent, "the parent of a constant has to be a constant");
          if (this.Equals(p.Parent.Decl))
            rc.Error(p.Parent, "constant cannot be its own parent");
        }
      }

      // check that no parent occurs twice
      // (could be optimised)
      if (Parents != null) {
        for (int i = 0; i < Parents.Count; ++i) {
          if (Parents[i].Parent.Decl != null) {
            for (int j = i + 1; j < Parents.Count; ++j) {
              if (Parents[j].Parent.Decl != null &&
                  ((!)Parents[i].Parent.Decl).Equals(Parents[j].Parent.Decl))
                rc.Error(Parents[j].Parent,
                         "{0} occurs more than once as parent",
                         Parents[j].Parent.Decl);
            }
          }
        }
      }
    }
    public override void Typecheck(TypecheckingContext! tc) 
    {
      base.Typecheck(tc);

      if (Parents != null) {
        foreach (ConstantParent! p in Parents) {
          p.Parent.Typecheck(tc);
          if (!((!)p.Parent.Decl).TypedIdent.Type.Unify(this.TypedIdent.Type))
            tc.Error(p.Parent,
                     "parent of constant has incompatible type ({0} instead of {1})",
                     p.Parent.Decl.TypedIdent.Type, this.TypedIdent.Type);
        }
      }
    }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitConstant(this);
    }
  }
  public class GlobalVariable : Variable
  {
    public GlobalVariable(IToken! tok, TypedIdent! typedIdent)
      : base(tok, typedIdent)
    {
    }
    public GlobalVariable(IToken! tok, TypedIdent! typedIdent, QKeyValue kv)
      : base(tok, typedIdent, kv)
    {
    }
    public override bool IsMutable
    {
      get
      {
        return true;
      }
    }
    public override void Register(ResolutionContext! rc)
    {
      rc.AddVariable(this, true);
    }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitGlobalVariable(this);
    }
  }
  public class Formal : Variable
  {
    public bool InComing;
    public Formal(IToken! tok, TypedIdent! typedIdent, bool incoming)
      : base(tok, typedIdent)
    {
      InComing = incoming;
    }
    public override bool IsMutable
    {
      get
      {
        return !InComing;
      }
    }
    public override void Register(ResolutionContext! rc)
    {
      rc.AddVariable(this, false);
    }

    /// <summary>
    /// Given a sequence of Formal declarations, returns sequence of Formals like the given one but without where clauses.
    /// The Type of each Formal is cloned.
    /// </summary>
    public static VariableSeq! StripWhereClauses(VariableSeq! w)
    {
      VariableSeq s = new VariableSeq();
      foreach (Variable! v in w) {
        Formal f = (Formal)v;
        TypedIdent ti = f.TypedIdent;
        s.Add(new Formal(f.tok, new TypedIdent(ti.tok, ti.Name, ti.Type.CloneUnresolved()), f.InComing));
      }
      return s;
    }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitFormal(this);
    }
  }
  public class LocalVariable : Variable
  {
    public LocalVariable(IToken! tok, TypedIdent! typedIdent, QKeyValue kv)
    {
      base(tok, typedIdent, kv);
    }
    public LocalVariable(IToken! tok, TypedIdent! typedIdent)
    {
      base(tok, typedIdent, null);
    }
    public override bool IsMutable
    {
      get
      {
        return true;
      }
    }
    public override void Register(ResolutionContext! rc)
    {
      rc.AddVariable(this, false);
    }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitLocalVariable(this);
    }
  }
  public class Incarnation : LocalVariable
  {
    public int incarnationNumber;
    public Incarnation(Variable! var, int i) :
      base(
      var.tok,
      new TypedIdent(var.TypedIdent.tok,var.TypedIdent.Name + "@" + i,var.TypedIdent.Type)
      )
    {
      incarnationNumber = i;
    }

  }
  public class BoundVariable : Variable
  {
    public BoundVariable(IToken! tok, TypedIdent! typedIdent)
      requires typedIdent.WhereExpr == null;
    {
      base(tok, typedIdent);  // here for aesthetic reasons
    }
    public override bool IsMutable
    {
      get
      {
        return false;
      }
    }
    public override void Register(ResolutionContext! rc)
    {
      rc.AddVariable(this, false);
    }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitBoundVariable(this);
    }
  }

  public abstract class DeclWithFormals : NamedDeclaration
  {
    public TypeVariableSeq! TypeParameters;
    public /*readonly--except in StandardVisitor*/ VariableSeq! InParams, OutParams;

    public DeclWithFormals (IToken! tok, string! name, TypeVariableSeq! typeParams,
                            VariableSeq! inParams, VariableSeq! outParams)
      : base(tok, name)
    {
      this.TypeParameters = typeParams;
      this.InParams = inParams;
      this.OutParams = outParams;
      // base(tok, name);
    }

    protected DeclWithFormals (DeclWithFormals! that)
      : base(that.tok, (!) that.Name)
    {
      this.TypeParameters = that.TypeParameters;
      this.InParams = that.InParams;
      this.OutParams = that.OutParams;
      // base(that.tok, (!) that.Name);
    }

    protected void EmitSignature (TokenTextWriter! stream, bool shortRet)
    {
      Type.EmitOptionalTypeParams(stream, TypeParameters);
      stream.Write("(");
      InParams.Emit(stream);
      stream.Write(")");

      if (shortRet) 
      {
        assert OutParams.Length == 1;
        stream.Write(" : ");
        ((!)OutParams[0]).TypedIdent.Type.Emit(stream);
      } 
      else if (OutParams.Length > 0)
      {
        stream.Write(" returns (");
        OutParams.Emit(stream);
        stream.Write(")");
      }
    }

    // Register all type parameters at the resolution context
    protected void RegisterTypeParameters(ResolutionContext! rc) {
      foreach (TypeVariable! v in TypeParameters)
        rc.AddTypeBinder(v);
    }

    protected void SortTypeParams() {
      TypeSeq! allTypes = InParams.ToTypeSeq;
      allTypes.AddRange(OutParams.ToTypeSeq);
      TypeParameters = Type.SortTypeParams(TypeParameters, allTypes, null);
    }

    /// <summary>
    /// Adds the given formals to the current variable context, and then resolves
    /// the types of those formals.  Does NOT resolve the where clauses of the
    /// formals.
    /// Relies on the caller to first create, and later tear down, that variable
    /// context.
    /// </summary>
    /// <param name="rc"></param>
    protected void RegisterFormals(VariableSeq! formals, ResolutionContext! rc)
    {
      foreach (Formal! f in formals)
      {
        if (f.Name != TypedIdent.NoName)
        {
          rc.AddVariable(f, false);
        }
        f.Resolve(rc);
      }
    }

    /// <summary>
    /// Resolves the where clauses (and attributes) of the formals.
    /// </summary>
    /// <param name="rc"></param>
    protected void ResolveFormals(VariableSeq! formals, ResolutionContext! rc)
    {
      foreach (Formal! f in formals)
      {
        f.ResolveWhere(rc);
      }
    }

    public override void Typecheck(TypecheckingContext! tc) {
      TypecheckAttributes(tc);
      foreach (Formal! p in InParams) {
        p.Typecheck(tc);
      }
      foreach (Formal! p in OutParams) {
        p.Typecheck(tc);
      }
    }
  }

  public class Expansion {
    public string? ignore; // when to ignore
    public Expr! body;
    public TypeVariableSeq! TypeParameters;
    public Variable[]! formals;

    public Expansion(string? ignore, Expr! body,
                     TypeVariableSeq! typeParams, Variable[]! formals)
    {
      this.ignore = ignore;
      this.body = body;
      this.TypeParameters = typeParams;
      this.formals = formals;
    }
  }

  public class Function : DeclWithFormals
  {
    public string? Comment;

    // the body is only set if the function is declared with {:expand true}
    public Expr Body;
    public List<Expansion!>? expansions;
    public bool doingExpansion;

    private bool neverTrigger;
    private bool neverTriggerComputed;

    public Function(IToken! tok, string! name, VariableSeq! args, Variable! result)
    {
      this(tok, name, new TypeVariableSeq(), args, result, null);
    }
    public Function(IToken! tok, string! name, TypeVariableSeq! typeParams, VariableSeq! args, Variable! result)
    {
      this(tok, name, typeParams, args, result, null);
    }
    public Function(IToken! tok, string! name, VariableSeq! args, Variable! result,
                    string? comment)
    {
      this(tok, name, new TypeVariableSeq(), args, result, comment);
    }
    public Function(IToken! tok, string! name, TypeVariableSeq! typeParams, VariableSeq! args, Variable! result,
                    string? comment)
      : base(tok, name, typeParams, args, new VariableSeq(result))
    {
      Comment = comment;
      // base(tok, name, args, new VariableSeq(result));
    }
    public Function(IToken! tok, string! name, TypeVariableSeq! typeParams, VariableSeq! args, Variable! result,
                    string? comment, QKeyValue kv)
    {
      this(tok, name, typeParams, args, result, comment);
      this.Attributes = kv;
    }
    public override void Emit(TokenTextWriter! stream, int level)
    {
      if (Comment != null) {
        stream.WriteLine(this, level, "// " + Comment);
      }
      stream.Write(this, level, "function ");
      EmitAttributes(stream);
      if (CommandLineOptions.Clo.PrintWithUniqueASTIds) {
        stream.Write("h{0}^^{1}", this.GetHashCode(), TokenTextWriter.SanitizeIdentifier(this.Name));
      } else {
        stream.Write("{0}", TokenTextWriter.SanitizeIdentifier(this.Name));
      }
      EmitSignature(stream, true);
      if (Body != null) {
        stream.WriteLine();
        stream.WriteLine("{");
        stream.Write(level+1, "");
        Body.Emit(stream);
        stream.WriteLine();
        stream.WriteLine("}");
      } else {
        stream.WriteLine(";");
      }
    }
    public override void Register(ResolutionContext! rc)
    {
      rc.AddProcedure(this);
    }
    public override void Resolve(ResolutionContext! rc)
    {
      int previousTypeBinderState = rc.TypeBinderState;
      try {
        RegisterTypeParameters(rc);
        rc.PushVarContext();
        RegisterFormals(InParams, rc);
        RegisterFormals(OutParams, rc);
        ResolveAttributes(rc);
        if (Body != null)
          Body.Resolve(rc);
        rc.PopVarContext();
        Type.CheckBoundVariableOccurrences(TypeParameters,
                                           InParams.ToTypeSeq, OutParams.ToTypeSeq,
                                           this.tok, "function arguments",
                                           rc);
      } finally {
        rc.TypeBinderState = previousTypeBinderState;
      }
      SortTypeParams();
    }
    public override void Typecheck(TypecheckingContext! tc)
    {
      // PR: why was the base call left out previously?
      base.Typecheck(tc);
      // TypecheckAttributes(tc);
      if (Body != null) {
        Body.Typecheck(tc);
        if (!((!)Body.Type).Unify(((!)OutParams[0]).TypedIdent.Type))
          tc.Error(Body,
                   "function body with invalid type: {0} (expected: {1})",
                   Body.Type, ((!)OutParams[0]).TypedIdent.Type);
      }
    }

    public bool NeverTrigger
    {
      get {
        if (!neverTriggerComputed) {
          this.CheckBooleanAttribute("never_pattern", ref neverTrigger);
          neverTriggerComputed = true;
        }
        return neverTrigger;
      }
    }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitFunction(this);
    }
  }

  public class Requires : Absy, IPotentialErrorNode
  {
    public readonly bool Free;
    public Expr! Condition;
    public string? Comment;

    // TODO: convert to use generics
    private object errorData;
    public object ErrorData {
      get { return errorData; }
      set { errorData = value; }
    }
    invariant errorData != null ==> errorData is string;

    private MiningStrategy errorDataEnhanced;
    public MiningStrategy ErrorDataEnhanced {
      get { return errorDataEnhanced; }
      set { errorDataEnhanced = value; }
    }

    public QKeyValue Attributes;

    public String ErrorMessage {
      get {
        return QKeyValue.FindStringAttribute(Attributes, "msg");
      }
    }

    public Requires(IToken! token, bool free, Expr! condition, string? comment, QKeyValue kv)
      : base(token)
    {
      this.Free = free;
      this.Condition = condition;
      this.Comment = comment;
      this.Attributes = kv;
      // base(token);
    }

    public Requires(IToken! token, bool free, Expr! condition, string? comment)
    {
      this(token, free, condition, comment, null);
    }

    public Requires(bool free, Expr! condition)
    {
      this(Token.NoToken, free, condition, null);
    }

    public Requires(bool free, Expr! condition, string? comment)
    {
      this(Token.NoToken, free, condition, comment);
    }

    public void Emit(TokenTextWriter! stream, int level)
    {
      if (Comment != null) {
        stream.WriteLine(this, level, "// " + Comment);
      }
      stream.Write(this, level, "{0}requires ", Free ? "free " : "");
      this.Condition.Emit(stream);
      stream.WriteLine(";");
    }

    public override void Resolve(ResolutionContext! rc)
    {
      this.Condition.Resolve(rc);
    }

    public override void Typecheck(TypecheckingContext! tc)
    {
      this.Condition.Typecheck(tc);
      assert this.Condition.Type != null;  // follows from postcondition of Expr.Typecheck
      if (!this.Condition.Type.Unify(Type.Bool)) 
      {
        tc.Error(this, "preconditions must be of type bool");
      }
    }
  }

  public class Ensures : Absy, IPotentialErrorNode
  {
    public readonly bool Free;
    public Expr! Condition;
    public string? Comment;

    // TODO: convert to use generics
    private object errorData;
    public object ErrorData {
      get { return errorData; }
      set { errorData = value; }
    }
    invariant errorData != null ==> errorData is string;

    private MiningStrategy errorDataEnhanced;
    public MiningStrategy ErrorDataEnhanced {
      get { return errorDataEnhanced; }
      set { errorDataEnhanced = value; }
    }

    public String ErrorMessage {
      get {
        return QKeyValue.FindStringAttribute(Attributes, "msg");
      }
    }

    public QKeyValue Attributes;

    public Ensures(IToken! token, bool free, Expr! condition, string? comment, QKeyValue kv)
      : base(token)
    {
      this.Free = free;
      this.Condition = condition;
      this.Comment = comment;
      this.Attributes = kv;
      // base(token);
    }

    public Ensures(IToken! token, bool free, Expr! condition, string? comment)
    {
      this(token, free, condition, comment, null);
    }

    public Ensures(bool free, Expr! condition)
    {
      this(Token.NoToken, free, condition, null);
    }

    public Ensures(bool free, Expr! condition, string? comment)
    {
      this(Token.NoToken, free, condition, comment);
    }

    public void Emit(TokenTextWriter! stream, int level)
    {
      if (Comment != null) {
        stream.WriteLine(this, level, "// " + Comment);
      }
      stream.Write(this, level, "{0}ensures ", Free ? "free " : "");
      this.Condition.Emit(stream);
      stream.WriteLine(";");
    }

    public override void Resolve(ResolutionContext! rc)
    {
      this.Condition.Resolve(rc);
    }

    public override void Typecheck(TypecheckingContext! tc)
    {
      this.Condition.Typecheck(tc);
      assert this.Condition.Type != null;  // follows from postcondition of Expr.Typecheck
      if (!this.Condition.Type.Unify(Type.Bool)) 
      {
        tc.Error(this, "postconditions must be of type bool");
      }
    }
  }

  public class Procedure : DeclWithFormals
  {
    public RequiresSeq! Requires;
    public IdentifierExprSeq! Modifies;
    public EnsuresSeq! Ensures;

    // Abstract interpretation:  Procedure-specific invariants...
    [Rep]
    public readonly ProcedureSummary! Summary;

    public Procedure (
      IToken! tok, 
      string! name, 
      TypeVariableSeq! typeParams,
      VariableSeq! inParams, 
      VariableSeq! outParams,
      RequiresSeq! @requires,
      IdentifierExprSeq! @modifies,
      EnsuresSeq! @ensures
      )
    {
      this(tok, name, typeParams, inParams, outParams, @requires, @modifies, @ensures, null);
    }

    public Procedure (
      IToken! tok, 
      string! name, 
      TypeVariableSeq! typeParams,
      VariableSeq! inParams, 
      VariableSeq! outParams,
      RequiresSeq! @requires,
      IdentifierExprSeq! @modifies,
      EnsuresSeq! @ensures,
      QKeyValue kv
      )
      : base(tok, name, typeParams, inParams, outParams)
    {
      this.Requires = @requires;
      this.Modifies = @modifies;
      this.Ensures = @ensures;
      this.Summary = new ProcedureSummary();
      this.Attributes = kv;
    }
    
    public override void Emit(TokenTextWriter! stream, int level)
    {
      stream.Write(this, level, "procedure ");
      EmitAttributes(stream);
      stream.Write(this, level, "{0}", TokenTextWriter.SanitizeIdentifier(this.Name));
      EmitSignature(stream, false);
      stream.WriteLine(";");

      level++;

      foreach (Requires! e in this.Requires)
      {
        e.Emit(stream, level);
      }

      if (this.Modifies.Length > 0)
      {
        stream.Write(level, "modifies ");
        this.Modifies.Emit(stream, false);
        stream.WriteLine(";");
      }

      foreach (Ensures! e in this.Ensures)
      {
        e.Emit(stream, level);
      }

      if (!CommandLineOptions.Clo.IntraproceduralInfer)
      {
        for (int s=0; s < this.Summary.Count; s++)
        {
          ProcedureSummaryEntry! entry = (!) this.Summary[s];
          stream.Write(level + 1, "// ");
          Expr e;
          e = (Expr)entry.Lattice.ToPredicate(entry.OnEntry);
          e.Emit(stream);
          stream.Write("   ==>   ");
          e = (Expr)entry.Lattice.ToPredicate(entry.OnExit);
          e.Emit(stream);
          stream.WriteLine();
        }
      }

      stream.WriteLine();
      stream.WriteLine();
    }

    public override void Register(ResolutionContext! rc)
    {
      rc.AddProcedure(this);
    }
    public override void Resolve(ResolutionContext! rc)
    {
      rc.PushVarContext();

      foreach (IdentifierExpr! ide in Modifies)
      {
        ide.Resolve(rc);
      }

      int previousTypeBinderState = rc.TypeBinderState;
      try {
        RegisterTypeParameters(rc);

        RegisterFormals(InParams, rc);
        ResolveFormals(InParams, rc);  // "where" clauses of in-parameters are resolved without the out-parameters in scope
        foreach (Requires! e in Requires) 
        {
          e.Resolve(rc);
        }
        RegisterFormals(OutParams, rc);
        ResolveFormals(OutParams, rc);  // "where" clauses of out-parameters are resolved with both in- and out-parametes in scope
      
        rc.StateMode = ResolutionContext.State.Two;
        foreach (Ensures! e in Ensures) 
        {
          e.Resolve(rc);
        }
        rc.StateMode = ResolutionContext.State.Single;
        ResolveAttributes(rc);

        Type.CheckBoundVariableOccurrences(TypeParameters,
                                           InParams.ToTypeSeq, OutParams.ToTypeSeq,
                                           this.tok, "procedure arguments",
                                           rc);

      } finally {
        rc.TypeBinderState = previousTypeBinderState;
      }

      rc.PopVarContext();

      SortTypeParams();
    }
    public override void Typecheck(TypecheckingContext! tc)
    {
      base.Typecheck(tc);
      foreach (IdentifierExpr! ide in Modifies)
      {
        assume ide.Decl != null;
        if (!ide.Decl.IsMutable)
        {
          tc.Error(this, "modifies list contains constant: {0}", ide.Name);
        }
        ide.Typecheck(tc);
      }
      foreach (Requires! e in Requires)
      {
        e.Typecheck(tc);
      }
      foreach (Ensures! e in Ensures)
      {
        e.Typecheck(tc);
      }
    }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitProcedure(this);
    }
  }

  public class Implementation : DeclWithFormals
  {
    public VariableSeq! LocVars;
    [Rep] public StmtList StructuredStmts;
    [Rep] public List<Block!>! Blocks;
    public Procedure Proc;

    // Blocks before applying passification etc.
    // Both are used only when /inline is set.
    public List<Block!>? OriginalBlocks;
    public VariableSeq? OriginalLocVars;

    // Strongly connected components
    private StronglyConnectedComponents<Block!> scc;
    private bool BlockPredecessorsComputed;
    public bool StronglyConnectedComponentsComputed
    {
      get
      {
        return this.scc != null;
      }
    }

    public bool SkipVerification
    {
      get
      {
        bool verify = true;
        ((!)this.Proc).CheckBooleanAttribute("verify", ref verify);
        this.CheckBooleanAttribute("verify", ref verify);
        if (!verify) {
          return true;
        }

        if (CommandLineOptions.Clo.ProcedureInlining == CommandLineOptions.Inlining.Assert ||
            CommandLineOptions.Clo.ProcedureInlining == CommandLineOptions.Inlining.Assume) {
          Expr? inl = this.FindExprAttribute("inline");
          if (inl == null) inl = this.Proc.FindExprAttribute("inline");
          if (inl != null && inl is LiteralExpr && ((LiteralExpr)inl).isBigNum && ((LiteralExpr)inl).asBigNum.Signum > 0) {
            return true;
          }
        }

        return false;
      }
    }

    public Implementation(IToken! tok, string! name, TypeVariableSeq! typeParams,
                          VariableSeq! inParams, VariableSeq! outParams,
                          VariableSeq! localVariables, [Captured] StmtList! structuredStmts)
    {
      this(tok, name, typeParams, inParams, outParams, localVariables, structuredStmts, null, new Errors());
    }

    public Implementation(IToken! tok, string! name, TypeVariableSeq! typeParams,
                          VariableSeq! inParams, VariableSeq! outParams,
                          VariableSeq! localVariables, [Captured] StmtList! structuredStmts,
                          Errors! errorHandler)
    {
      this(tok, name, typeParams, inParams, outParams, localVariables, structuredStmts, null, errorHandler);
    }

    public Implementation(IToken! tok, string! name, TypeVariableSeq! typeParams,
                          VariableSeq! inParams, VariableSeq! outParams,
                          VariableSeq! localVariables, [Captured] StmtList! structuredStmts, QKeyValue kv,
                          Errors! errorHandler)
      : base(tok, name, typeParams, inParams, outParams)
    {
      LocVars = localVariables;
      StructuredStmts = structuredStmts;
      BigBlocksResolutionContext ctx = new BigBlocksResolutionContext(structuredStmts, errorHandler);
      Blocks = ctx.Blocks;
      BlockPredecessorsComputed = false;
      scc = null;
      Attributes = kv;

      // base(tok, name, inParams, outParams);
    }

    public Implementation(IToken! tok, string! name, TypeVariableSeq! typeParams,
                          VariableSeq! inParams, VariableSeq! outParams,
                          VariableSeq! localVariables, [Captured] List<Block!>! block)
    {
      this(tok, name, typeParams, inParams, outParams, localVariables, block, null);
    }

    public Implementation(IToken! tok, string! name, TypeVariableSeq! typeParams,
                          VariableSeq! inParams, VariableSeq! outParams,
                          VariableSeq! localVariables, [Captured] List<Block!>! blocks, QKeyValue kv)
      : base(tok, name, typeParams, inParams, outParams)
    {
      LocVars = localVariables;
      Blocks = blocks;
      BlockPredecessorsComputed = false;
      scc = null;
      Attributes = kv;

      //base(tok, name, inParams, outParams);
    }

    public override void Emit(TokenTextWriter! stream, int level)
    {
      stream.Write(this, level, "implementation ");
      EmitAttributes(stream);
      stream.Write(this, level, "{0}", TokenTextWriter.SanitizeIdentifier(this.Name));
      EmitSignature(stream, false);
      stream.WriteLine();

      stream.WriteLine(level, "{0}", '{');

      foreach (Variable! v in this.LocVars) {
        v.Emit(stream, level + 1);
      }

      if (this.StructuredStmts != null && !CommandLineOptions.Clo.PrintInstrumented && !CommandLineOptions.Clo.PrintInlined) {
        if (this.LocVars.Length > 0) {
          stream.WriteLine();
        }
        if (CommandLineOptions.Clo.PrintUnstructured < 2) {
          if (CommandLineOptions.Clo.PrintUnstructured == 1) {
            stream.WriteLine(this, level+1, "/*** structured program:");
          }
          this.StructuredStmts.Emit(stream, level+1);
          if (CommandLineOptions.Clo.PrintUnstructured == 1) {
            stream.WriteLine(level+1, "**** end structured program */");
          }
        }
      }

      if (this.StructuredStmts == null || 1 <= CommandLineOptions.Clo.PrintUnstructured ||
          CommandLineOptions.Clo.PrintInstrumented || CommandLineOptions.Clo.PrintInlined)
      {
        foreach (Block b in this.Blocks)
        {
          b.Emit(stream, level+1);
        }
      }

      stream.WriteLine(level, "{0}", '}');

      stream.WriteLine();
      stream.WriteLine();
    }
    public override void Register(ResolutionContext! rc)
    {
      // nothing to register
    }
    public override void Resolve(ResolutionContext! rc)
    {
      if (Proc != null)
      {
        // already resolved
        return;
      }
      DeclWithFormals dwf = rc.LookUpProcedure((!) this.Name);
      Proc = dwf as Procedure;
      if (dwf == null)
      {
        rc.Error(this, "implementation given for undeclared procedure: {0}", this.Name);
      }
      else if (Proc == null)
      {
        rc.Error(this, "implementations given for function, not procedure: {0}", this.Name);
      }

      int previousTypeBinderState = rc.TypeBinderState;
      try {
        RegisterTypeParameters(rc);

        rc.PushVarContext();
        RegisterFormals(InParams, rc);
        RegisterFormals(OutParams, rc);

        foreach (Variable! v in LocVars) 
        {
          v.Register(rc);
          v.Resolve(rc);
        }
        foreach (Variable! v in LocVars) 
        {
          v.ResolveWhere(rc);
        }

        rc.PushProcedureContext();
        foreach (Block b in Blocks) 
        {
          b.Register(rc);
        }
      
        ResolveAttributes(rc);

        rc.StateMode = ResolutionContext.State.Two;
        foreach (Block b in Blocks) 
        {
          b.Resolve(rc);
        }
        rc.StateMode = ResolutionContext.State.Single;
      
        rc.PopProcedureContext();
        rc.PopVarContext();

        Type.CheckBoundVariableOccurrences(TypeParameters,
                                           InParams.ToTypeSeq, OutParams.ToTypeSeq,
                                           this.tok, "implementation arguments",
                                           rc);
      } finally {
        rc.TypeBinderState = previousTypeBinderState;
      }
      SortTypeParams();
    }
    public override void Typecheck(TypecheckingContext! tc)
    {
      base.Typecheck(tc);

      assume this.Proc != null;

      if (this.TypeParameters.Length != Proc.TypeParameters.Length) {
        tc.Error(this, "mismatched number of type parameters in procedure implementation: {0}",
                 this.Name);
      } else {
        // if the numbers of type parameters are different, it is
        // difficult to compare the argument types
        MatchFormals(this.InParams, Proc.InParams, "in", tc);
        MatchFormals(this.OutParams, Proc.OutParams, "out", tc);
      }

      foreach (Variable! v in LocVars) 
      {
        v.Typecheck(tc);
      }
      IdentifierExprSeq oldFrame = tc.Frame;
      tc.Frame = Proc.Modifies;
      foreach (Block b in Blocks)
      {
        b.Typecheck(tc);
      }
      assert tc.Frame == Proc.Modifies;
      tc.Frame = oldFrame;
    }
    void MatchFormals(VariableSeq! implFormals, VariableSeq! procFormals,
                      string! inout, TypecheckingContext! tc) 
    {
      if (implFormals.Length != procFormals.Length)
      {
        tc.Error(this, "mismatched number of {0}-parameters in procedure implementation: {1}",
                 inout, this.Name);
      } 
      else 
      {
        // unify the type parameters so that types can be compared
        assert Proc != null;
        assert this.TypeParameters.Length == Proc.TypeParameters.Length;
        
        IDictionary<TypeVariable!, Type!>! subst1 =
          new Dictionary<TypeVariable!, Type!> ();
        IDictionary<TypeVariable!, Type!>! subst2 =
          new Dictionary<TypeVariable!, Type!> ();

        for (int i = 0; i < this.TypeParameters.Length; ++i) {
          TypeVariable! newVar =
            new TypeVariable (Token.NoToken, Proc.TypeParameters[i].Name);
          subst1.Add(Proc.TypeParameters[i], newVar);
          subst2.Add(this.TypeParameters[i], newVar);
        }

        for (int i = 0; i < implFormals.Length; i++) 
        {
          // the names of the formals are allowed to change from the proc to the impl

          // but types must be identical
          Type t = ((Variable!)implFormals[i]).TypedIdent.Type.Substitute(subst2);
          Type u = ((Variable!)procFormals[i]).TypedIdent.Type.Substitute(subst1);
          if (!t.Equals(u)) 
          {
            string! a = (!) ((Variable!)implFormals[i]).Name;
            string! b = (!) ((Variable!)procFormals[i]).Name;
            string! c;
            if (a == b) {
              c = a;
            } else {
              c = String.Format("{0} (named {1} in implementation)", b, a);
            }
            tc.Error(this, "mismatched type of {0}-parameter in implementation {1}: {2}", inout, this.Name, c);
          }
        }
      }
    }

    private Hashtable/*Variable->Expr*//*?*/ formalMap = null;
    public void ResetImplFormalMap() {
      this.formalMap = null;
    }
    public Hashtable /*Variable->Expr*/! GetImplFormalMap()
    {
      if (this.formalMap != null)
        return this.formalMap;
      else
      {
        Hashtable /*Variable->Expr*/! map = new Hashtable /*Variable->Expr*/ (InParams.Length + OutParams.Length);

        assume this.Proc != null;
        assume InParams.Length == Proc.InParams.Length;
        for (int i = 0; i < InParams.Length; i++)
        {
          Variable! v = (!) InParams[i];
          IdentifierExpr ie = new IdentifierExpr(v.tok, v);
          Variable! pv = (!) Proc.InParams[i];
          map.Add(pv, ie);
        }
        System.Diagnostics.Debug.Assert(OutParams.Length == Proc.OutParams.Length);
        for (int i = 0; i < OutParams.Length; i++)
        {
          Variable! v = (!) OutParams[i];
          IdentifierExpr ie = new IdentifierExpr(v.tok, v);
          Variable! pv = (!) Proc.OutParams[i];
          map.Add(pv, ie);
        }
        this.formalMap = map;

        if (CommandLineOptions.Clo.PrintWithUniqueASTIds)
        {
          Console.WriteLine("Implementation.GetImplFormalMap on {0}:", this.Name);
          using (TokenTextWriter stream = new TokenTextWriter("<console>", Console.Out, false))
          {
            foreach (DictionaryEntry e in map)
            {
              Console.Write("  ");
              ((Variable!)e.Key).Emit(stream, 0);
              Console.Write("  --> ");
              ((Expr!)e.Value).Emit(stream);
              Console.WriteLine();
            }
          }
        }

        return map;
      }
    }

    /// <summary>
    /// Instrument the blocks with the inferred invariants
    /// </summary>
    public override void InstrumentWithInvariants()
    {
      foreach (Block b in this.Blocks)
      {
        if (b.Lattice != null)
        {
          assert b.PreInvariant != null;      /* If the pre-abstract state is null, then something is wrong */
          assert b.PostInvariant != null;      /* If the post-state is null, then something is wrong */

          bool instrumentEntry;
          bool instrumentExit;
          switch (CommandLineOptions.Clo.InstrumentInfer) {
            case CommandLineOptions.InstrumentationPlaces.Everywhere:
              instrumentEntry = true;
              instrumentExit = true;
              break;
            case CommandLineOptions.InstrumentationPlaces.LoopHeaders:
              instrumentEntry = b.widenBlock;
              instrumentExit = false;
              break;
            default:
              assert false;  // unexpected InstrumentationPlaces value
          }
          
          if (instrumentEntry || instrumentExit) {
            CmdSeq newCommands = new CmdSeq();
            if (instrumentEntry) {
              Expr inv = (Expr) b.Lattice.ToPredicate(b.PreInvariant); /*b.PreInvariantBuckets.GetDisjunction(b.Lattice);*/
              PredicateCmd cmd = CommandLineOptions.Clo.InstrumentWithAsserts ? (PredicateCmd)new AssertCmd(Token.NoToken,inv) : (PredicateCmd)new AssumeCmd(Token.NoToken, inv);
              newCommands.Add(cmd);
            }
            newCommands.AddRange(b.Cmds);
            if (instrumentExit) {
              Expr inv = (Expr) b.Lattice.ToPredicate(b.PostInvariant);
              PredicateCmd cmd = CommandLineOptions.Clo.InstrumentWithAsserts ? (PredicateCmd)new AssertCmd(Token.NoToken,inv) : (PredicateCmd)new AssumeCmd(Token.NoToken, inv);
              newCommands.Add(cmd);
            }
            b.Cmds = newCommands;
          }
        }
      }
    }

    /// <summary>
    /// Return a collection of blocks that are reachable from the block passed as a parameter.
    /// The block must be defined in the current implementation
    /// </summary>
    public ICollection<Block!> GetConnectedComponents(Block! startingBlock)
    {
      assert this.Blocks.Contains(startingBlock);

      if(!this.BlockPredecessorsComputed)
        ComputeStronglyConnectedComponents();

#if  DEBUG_PRINT
      System.Console.WriteLine("* Strongly connected components * \n{0} \n ** ", scc);
#endif

      foreach(ICollection<Block!> component in (!) this.scc)
      {
        foreach(Block! b in component)
        {
          if(b == startingBlock)          // We found the compontent that owns the startingblock
          {
            return component;
          }
        }
      }

      assert false;   // if we are here, it means that the block is not in one of the components. This is an error.
    }

    /// <summary>
    /// Compute the strongly connected compontents of the blocks in the implementation.
    /// As a side effect, it also computes the "predecessor" relation for the block in the implementation
    /// </summary>
    override public void ComputeStronglyConnectedComponents()
    {
      if(!this.BlockPredecessorsComputed)
              ComputedPredecessorsForBlocks();

      Adjacency<Block!> next = new Adjacency<Block!>(Successors);
      Adjacency<Block!> prev = new Adjacency<Block!>(Predecessors);

      this.scc = new StronglyConnectedComponents<Block!>(this.Blocks, next, prev);
      scc.Compute();

      foreach(Block! block in this.Blocks)
      {
        block.Predecessors = new BlockSeq();
      }

    }

    /// <summary>
    /// Reset the abstract stated computed before
    /// </summary>
    override public void ResetAbstractInterpretationState()
    {
      foreach(Block! b in this.Blocks)
      {
        b.ResetAbstractInterpretationState();
      }
    }

    /// <summary>
    /// A private method used as delegate for the strongly connected components.
    /// It return, given a node, the set of its successors
    /// </summary>
    private IEnumerable/*<Block!>*/! Successors(Block! node)
    {
      GotoCmd gotoCmd = node.TransferCmd as GotoCmd;

      if(gotoCmd != null)
      { // If it is a gotoCmd
        assert gotoCmd.labelTargets != null;

        return gotoCmd.labelTargets;
      }
      else
      { // otherwise must be a ReturnCmd
        assert node.TransferCmd is ReturnCmd;

        return new List<Block!>();
      }
    }

    /// <summary>
    /// A private method used as delegate for the strongly connected components.
    /// It return, given a node, the set of its predecessors
    /// </summary>
    private IEnumerable/*<Block!>*/! Predecessors(Block! node)
    {
        assert this.BlockPredecessorsComputed;

        return node.Predecessors;
    }

    /// <summary>
    /// Compute the predecessor informations for the blocks
    /// </summary>
    private void ComputedPredecessorsForBlocks()
    {
      foreach (Block b in this.Blocks)
      {
        GotoCmd gtc = b.TransferCmd as GotoCmd;
        if (gtc != null)
        {
          assert gtc.labelTargets != null;
          foreach (Block! dest in gtc.labelTargets)
          {
            dest.Predecessors.Add(b);
          }
        }
      }
      this.BlockPredecessorsComputed = true;
    }

    public void PruneUnreachableBlocks() {
      ArrayList /*Block!*/ visitNext = new ArrayList /*Block!*/ ();
      List<Block!> reachableBlocks = new List<Block!>();
      System.Compiler.IMutableSet /*Block!*/ reachable = new System.Compiler.HashSet /*Block!*/ ();  // the set of elements in "reachableBlocks"

      visitNext.Add(this.Blocks[0]);
      while (visitNext.Count != 0) {
        Block! b = (Block!)visitNext[visitNext.Count-1];
        visitNext.RemoveAt(visitNext.Count-1);
        if (!reachable.Contains(b)) {
            reachableBlocks.Add(b);
            reachable.Add(b);
            if (b.TransferCmd is GotoCmd) {
                foreach (Cmd! s in b.Cmds) {
                    if (s is PredicateCmd) {
                        LiteralExpr e = ((PredicateCmd)s).Expr as LiteralExpr;
                        if (e != null && e.IsFalse) {
                            // This statement sequence will never reach the end, because of this "assume false" or "assert false".
                            // Hence, it does not reach its successors.
                            b.TransferCmd = new ReturnCmd(b.TransferCmd.tok);
                            goto NEXT_BLOCK;
                        }
                    }
                }
                // it seems that the goto statement at the end may be reached
                foreach (Block! succ in (!)((GotoCmd)b.TransferCmd).labelTargets) {
                    visitNext.Add(succ);
                }
            }
        }
        NEXT_BLOCK: {}
      }

      this.Blocks = reachableBlocks;
    }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitImplementation(this);
    }
  }


  public class TypedIdent : Absy
  {
    public const string NoName = "";
    public string! Name;
    public Type! Type;
    public Expr WhereExpr;
      // [NotDelayed]
    public TypedIdent (IToken! tok, string! name, Type! type)
      ensures this.WhereExpr == null;  //PM: needed to verify BoogiePropFactory.FreshBoundVariable
    {
      this(tok, name, type, null); // here for aesthetic reasons
    }
    // [NotDelayed]
    public TypedIdent (IToken! tok, string! name, Type! type, Expr whereExpr)
      : base(tok)
      ensures this.WhereExpr == whereExpr;
    {
      this.Name = name;
      this.Type = type;
      this.WhereExpr = whereExpr;
      // base(tok);
    }
    public bool HasName {
      get {
        return this.Name != NoName;
      }
    }
    public void Emit(TokenTextWriter! stream)
    {
      stream.SetToken(this);
      if (this.Name != NoName)
      {
        stream.Write("{0}: ", TokenTextWriter.SanitizeIdentifier(this.Name));
      }
      this.Type.Emit(stream);
      if (this.WhereExpr != null)
      {
        stream.Write(" where ");
        this.WhereExpr.Emit(stream);
      }
    }
    public override void Resolve(ResolutionContext! rc)
    {
      // NOTE: WhereExpr needs to be resolved by the caller, because the caller must provide a modified ResolutionContext
      this.Type = this.Type.ResolveType(rc);
    }
    public override void Typecheck(TypecheckingContext! tc)
    {
//   type variables can occur when working with polymorphic functions/procedures
//      if (!this.Type.IsClosed)
//        tc.Error(this, "free variables in type of an identifier: {0}",
//                 this.Type.FreeVariables);
      if (this.WhereExpr != null) {
        this.WhereExpr.Typecheck(tc);
        assert this.WhereExpr.Type != null;  // follows from postcondition of Expr.Typecheck
        if (!this.WhereExpr.Type.Unify(Type.Bool)) 
        {
          tc.Error(this, "where clauses must be of type bool");
        }
      }
    }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitTypedIdent(this);
    }
  }

  /// <summary>
  /// Conceptually, a LatticeElementList is a infinite array indexed from 0,
  /// where some finite number of elements have a non-null value.  All elements
  /// have type Lattice.Element.
  ///
  /// The Count property returns the first index above all non-null values.
  ///
  /// The [i] getter returns the element at position i, which may be null.  The
  /// index i is not allowed to be negative.
  /// The [i] setter sets the element at position i.  As a side effect, this
  /// operation may increase Count.  The index i is not allowed to be negative.
  /// The right-hand value of the setter is not allowed to be null; that is,
  /// null can occur in the list only as an "unused" element.
  /// </summary>
  public class LatticeElementList : ArrayList
  {
    public new /*Maybe null*/ AI.Lattice.Element this [ int i ]
    {
      get
      {
        if (i < Count)
        {
          return (AI.Lattice.Element)base[i];
        }
        else
        {
          return null;
        }
      }
      set
      {
        System.Diagnostics.Debug.Assert(value != null);
        while (Count <= i)
        {
          Add(null);
        }
        base[i] = value;
      }
    }
    /// <summary>
    /// Returns the disjunction of (the expression formed from) the
    /// non-null lattice elements in the list.  The expressions are
    /// formed according to the given "lattice", which is assumed to
    /// be the lattice of the lattice elements stored in the list.
    /// </summary>
    /// <param name="lattice"></param>
    /// <returns></returns>
    public Expr GetDisjunction(AI.Lattice! lattice)
    {
      Expr disjunction = null;
      foreach (AI.Lattice.Element el in this)
      {
        if (el != null)
        {
          Expr e = (Expr) lattice.ToPredicate(el);
          if (disjunction == null)
          {
            disjunction = e;
          }
          else
          {
            disjunction = Expr.Or(disjunction, e);
          }
        }
      }
      if (disjunction == null)
      {
        return Expr.False;
      }
      else
      {
        return disjunction;
      }
    }
  }



  public abstract class BoogieFactory {
    public static Expr! IExpr2Expr(AI.IExpr! e) {
      Variable v = e as Variable;
      if (v != null) {
        return new IdentifierExpr(Token.NoToken, v);
      }
      else if (e is AI.IVariable) { // but not a Variable
        return new AIVariableExpr(Token.NoToken, (AI.IVariable)e);
      }
      else if (e is IdentifierExpr.ConstantFunApp) {
        return ((IdentifierExpr.ConstantFunApp)e).IdentifierExpr;
      }
      else if (e is QuantifierExpr.AIQuantifier) {
        return ((QuantifierExpr.AIQuantifier)e).arg.RealQuantifier;
      }
      else {
        return (Expr)e;
      }
    }
    public static ExprSeq! IExprArray2ExprSeq(IList/*<AI.IExpr!>*/! a) {
      Expr[] e = new Expr[a.Count];
      int i = 0;
      foreach (AI.IExpr! aei in a) {
        e[i] = IExpr2Expr(aei);
        i++;
      }
      return new ExprSeq(e);
    }

    // Convert a Boogie type into an AIType if possible.  This should be
    // extended when AIFramework gets more types.
    public static AI.AIType! Type2AIType(Type! t)
    {
//      if (t.IsRef)
//        return AI.Ref.Type;
//      else
      if (t.IsInt)
        return AI.Int.Type;
//      else if (t.IsName)               PR: how to handle this case?
//        return AI.FieldName.Type;
      else
        return AI.Value.Type;
    }
  }

  #region Generic Sequences
  //---------------------------------------------------------------------
  // Generic Sequences
  //---------------------------------------------------------------------

  public sealed class TypedIdentSeq : PureCollections.Sequence
  {
    public TypedIdentSeq(params Type[]! args) : base(args) { }
    public new TypedIdent this[int index]
    {
      get
      {
        return (TypedIdent)base[index];
      }
      set
      {
        base[index] = value;
      }
    }
  }

  public sealed class RequiresSeq : PureCollections.Sequence
  {
    public RequiresSeq(params Requires[]! args) : base(args) { }
    public new Requires! this[int index]
    {
      get
      {
        return (Requires!) base[index];
      }
      set
      {
        base[index] = value;
      }
    }
  }

  public sealed class EnsuresSeq : PureCollections.Sequence
  {
    public EnsuresSeq(params Ensures[]! args) : base(args) { }
    public new Ensures! this[int index]
    {
      get
      {
        return (Ensures!) base[index];
      }
      set
      {
        base[index] = value;
      }
    }
  }

  public sealed class VariableSeq : PureCollections.Sequence
  {
    public VariableSeq(params Variable[]! args)
      : base(args)
    {
    }
    public VariableSeq(VariableSeq! varSeq)
      : base(varSeq)
    {
    }
    public new Variable this[int index]
    {
      get
      {
        return (Variable)base[index];
      }
      set
      {
        base[index] = value;
      }
    }
    public void Emit(TokenTextWriter! stream)
    {
      string sep = "";
      foreach (Variable! v in this)
      {
        stream.Write(sep);
        sep = ", ";
        v.EmitVitals(stream, 0);
      }
    }
    public TypeSeq! ToTypeSeq { get {
      TypeSeq! res = new TypeSeq ();
      foreach(Variable! v in this)
        res.Add(v.TypedIdent.Type);
      return res;
    } }
  }

  public sealed class TypeSeq : PureCollections.Sequence 
  {
    public TypeSeq(params Type[]! args)
      : base(args) 
    {
    }
    public TypeSeq(TypeSeq! varSeq) 
      : base(varSeq)
    {
    }
    public new Type! this[int index] 
    {
      get 
      {
        return (Type!)base[index];
      }
      set 
      {
        base[index] = value;
      }
    }
    public List<Type!>! ToList() {
      List<Type!>! res = new List<Type!> (Length);
      foreach (Type! t in this)
        res.Add(t);
      return res;
    }
    public void Emit(TokenTextWriter! stream, string! separator) 
    {
      string sep = "";
      foreach (Type! v in this) 
      {
        stream.Write(sep);
        sep = separator;
        v.Emit(stream);
      }
    }
  }

  public sealed class TypeVariableSeq : PureCollections.Sequence 
  {
    public TypeVariableSeq(params TypeVariable[]! args)
      : base(args) 
    {
    }
    public TypeVariableSeq(TypeVariableSeq! varSeq) 
      : base(varSeq)
    {
    }
/*  PR: the following two constructors cause Spec# crashes
    public TypeVariableSeq(TypeVariable! var) 
      : base(new TypeVariable! [] { var })
    {
    }
    public TypeVariableSeq() 
      : base(new TypeVariable![0])
    {
    } */
    public new TypeVariable! this[int index] 
    {
      get 
      {
        return (TypeVariable!)base[index];
      }
      set 
      {
        base[index] = value;
      }
    }
    public void AppendWithoutDups(TypeVariableSeq! s1) {
      for (int i = 0; i < s1.card; i++) {
        TypeVariable! next = s1[i];
        if (!this.Has(next)) this.Add(next);
      }
    }
    public void Emit(TokenTextWriter! stream, string! separator) 
    {
      string sep = "";
      foreach (TypeVariable! v in this) 
      {
        stream.Write(sep);
        sep = separator;
        v.Emit(stream);
      }
    }
    public new TypeVariable[]! ToArray() {
      TypeVariable[]! n = new TypeVariable[Length];
      int ct = 0;
      foreach (TypeVariable! var in this)
        n[ct++] = var;
      return n;
    }
    public List<TypeVariable!>! ToList() {
      List<TypeVariable!>! res = new List<TypeVariable!> (Length);
      foreach (TypeVariable! var in this)
        res.Add(var);
      return res;
    }
  }

  public sealed  class IdentifierExprSeq : PureCollections.Sequence 
  {
    public IdentifierExprSeq(params IdentifierExpr[]! args)
      : base(args)
    {
    }
    public IdentifierExprSeq(IdentifierExprSeq! ideSeq)
      : base(ideSeq)
    {
    }
    public new IdentifierExpr! this[int index]
    {
      get
      {
        return (IdentifierExpr!)base[index];
      }
      set
      {
          base[index] = value;
      }
    }

    public void Emit(TokenTextWriter! stream, bool printWhereComments)
    {
      string sep = "";
      foreach (IdentifierExpr! e in this)
      {
        stream.Write(sep);
        sep = ", ";
        e.Emit(stream);

        if (printWhereComments && e.Decl != null && e.Decl.TypedIdent.WhereExpr != null) {
          stream.Write(" /* where ");
          e.Decl.TypedIdent.WhereExpr.Emit(stream);
          stream.Write(" */");
        }
      }
    }
  }


  public sealed class CmdSeq : PureCollections.Sequence
  {
    public CmdSeq(params Cmd[]! args) : base(args){}
    public CmdSeq(CmdSeq! cmdSeq)
      : base(cmdSeq)
    {
    }
    public new Cmd! this[int index]
    {
      get
      {
        return (Cmd!)base[index];
      }
      set
      {
        base[index] = value;
      }
    }
  }

  public sealed class ExprSeq : PureCollections.Sequence
  {
    public ExprSeq(params Expr[]! args)
      : base(args)
    {
    }
    public ExprSeq(ExprSeq! exprSeq)
      : base(exprSeq)
    {
    }
    public new Expr this[int index]
    {
      get
      {
        return (Expr)base[index];
      }
      set
      {
        base[index] = value;
      }
    }
    
    public new Expr Last() { return (Expr)base.Last(); }

    public static ExprSeq operator +(ExprSeq a, ExprSeq b)
    {
        if (a==null) throw new ArgumentNullException("a");
        if (b==null) throw new ArgumentNullException("b");
        return Append(a,b);
    }

    public static ExprSeq Append(ExprSeq! s, ExprSeq! t)
    {
      Expr[] n = new Expr[s.card+t.card];
      for (int i = 0; i< s.card; i++) n[i] = s[i];
      for (int i = 0; i< t.card; i++) n[s.card+i] = t[i];
      return new ExprSeq(n);
    }
    public void Emit(TokenTextWriter! stream)
    {
      string sep = "";
      foreach (Expr! e in this)
      {
        stream.Write(sep);
        sep = ", ";
        e.Emit(stream);
      }
    }
    public TypeSeq! ToTypeSeq { get {
      TypeSeq! res = new TypeSeq ();
      foreach(Expr e in this)
        res.Add(((!)e).Type);
      return res;
    } }
  }

  public sealed class TokenSeq : PureCollections.Sequence
  {
    public TokenSeq(params Token[]! args)
      : base(args)
    {
    }
    public new Token this[int index]
    {
      get
      {
        return (Token)base[index];
      }
      set
      {
        base[index] = value;
      }
    }
  }

  public sealed class StringSeq : PureCollections.Sequence
  {
    public StringSeq(params string[]! args)
      : base(args)
    {
    }
    public new String this[int index]
    {
      get
      {
        return (String)base[index];
      }
      set
      {
        base[index] = value;
      }
    }
    public void Emit(TokenTextWriter! stream)
    {
      string sep = "";
      foreach (string! s in this)
      {
        stream.Write(sep);
        sep = ", ";
        stream.Write(s);
      }
    }
  }

  public sealed class BlockSeq : PureCollections.Sequence
  {
    public BlockSeq(params Block[]! args)
      : base(args)
    {
    }
    public BlockSeq(BlockSeq! blockSeq)
      : base(blockSeq)
    {
    }

    public new Block this[int index]
    {
      get
      {
        return (Block)base[index];
      }
      set
      {
        base[index] = value;
      }
    }
  }

  public static class Emitter {
    public static void Declarations(List<Declaration!>! decls, TokenTextWriter! stream)
    {
      bool first = true;
      foreach (Declaration d in decls)
      {
        if (d == null) continue;
        if (first)
        {
          first = false;
        }
        else
        {
          stream.WriteLine();
        }
        d.Emit(stream, 0);
      }
    }
  }
  public sealed class DeclarationSeq : PureCollections.Sequence
  {
    public DeclarationSeq(params string[]! args)
      : base(args)
    {
    }
    public new Declaration this[int index]
    {
      get
      {
        return (Declaration)base[index];
      }
      set
      {
        base[index] = value;
      }
    }
    public void Emit(TokenTextWriter! stream)
    {
      bool first = true;
      foreach (Declaration d in this)
      {
        if (d == null) continue;
        if (first)
        {
          first = false;
        }
        else
        {
          stream.WriteLine();
        }
        d.Emit(stream, 0);
      }
    }
    public void InstrumentWithInvariants ()
    {
      foreach (Declaration! d in this)
      {
        d.InstrumentWithInvariants();
      }
    }
  }
  #endregion


  #region Regular Expressions
  // a data structure to recover the "program structure" from the flow graph
  public sealed class RESeq : PureCollections.Sequence
  {
    public RESeq(params RE[]! args)
      : base (args)
    {
    }
    public RESeq(RESeq! reSeq)
      : base(reSeq)
    {
    }
    public new RE this[int index]
    {
      get
      {
        return (RE)base[index];
      }
      set
      {
        base[index] = value;
      }
    }
    //        public void Emit(TokenTextWriter stream)
    //        {
    //            string sep = "";
    //            foreach (RE e in this)
    //            {
    //                stream.Write(sep);
    //                sep = ", ";
    //                e.Emit(stream);
    //            }
    //        }
  }
  public abstract class RE : Cmd
  {
    public RE() : base(Token.NoToken) {}
    public override void AddAssignedVariables(VariableSeq! vars) { throw new NotImplementedException(); }
  }
  public class AtomicRE : RE
  {
    public Block! b;
    public AtomicRE(Block! block) { b = block; }
    public override void Resolve(ResolutionContext! rc)
    {
      b.Resolve(rc);
    }
    public override void Typecheck(TypecheckingContext! tc)
    {
      b.Typecheck(tc);
    }
    public override void Emit(TokenTextWriter! stream, int level)
    {
      b.Emit(stream,level);
    }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitAtomicRE(this);
    }
  }
  public abstract class CompoundRE : RE
  {
    public override void Resolve(ResolutionContext! rc)
    {
      return;
    }
    public override void Typecheck(TypecheckingContext! tc)
    {
      return;
    }
  }
  public class Sequential : CompoundRE
  {
    public RE! first;
    public RE! second;
    public Sequential(RE! a, RE! b)
    {
      first = a;
      second = b;
    }
    public override void Emit(TokenTextWriter! stream, int level)
    {
      stream.WriteLine();
      stream.WriteLine("{0};", Indent(level));
      first.Emit(stream,level+1);
      second.Emit(stream,level+1);
    }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitSequential(this);
    }
  }
  public class Choice : CompoundRE
  {
    public RESeq! rs;
    public Choice(RESeq! operands)
    {
      rs = operands;
      // base();
    }
    public override void Emit(TokenTextWriter! stream, int level)
    {
      stream.WriteLine();
      stream.WriteLine("{0}[]", Indent(level));
      foreach (RE! r in rs )
        r.Emit(stream,level+1);
    }

    public override Absy! StdDispatch(StandardVisitor! visitor)
    {
      return visitor.VisitChoice(this);
    }
  }
  public class DAG2RE
  {
    public static RE! Transform(Block! b)
    {
      TransferCmd tc = b.TransferCmd;
      if ( tc is ReturnCmd )
      {
        return new AtomicRE(b);
      }
      else if ( tc is GotoCmd )
      {
        GotoCmd! g = (GotoCmd) tc ;
        assume g.labelTargets != null;
        if ( g.labelTargets.Length == 1 )
        {
          return new Sequential(new AtomicRE(b),Transform( (!) g.labelTargets[0]));
        }
        else
        {
          RESeq rs = new RESeq();
          foreach (Block! target in g.labelTargets )
          {
            RE r = Transform(target);
            rs.Add(r);
          }
          RE second = new Choice(rs);
          return new Sequential(new AtomicRE(b),second);
        }
      }
      else
      {
        assume false;
        return new AtomicRE(b);
      }
    }
  }

  #endregion

  // NOTE: This class is here for convenience, since this file's
  // classes are used pretty much everywhere.

  public class BoogieDebug
  {
    public static bool DoPrinting = false;

    public static void Write (string! format, params object[]! args)
    {
      if (DoPrinting) { Console.Error.Write(format, args); }
    }

    public static void WriteLine (string! format, params object[]! args)
    {
      if (DoPrinting) { Console.Error.WriteLine(format, args); }
    }

    public static void WriteLine () { if (DoPrinting) { Console.Error.WriteLine(); } }
  }

}