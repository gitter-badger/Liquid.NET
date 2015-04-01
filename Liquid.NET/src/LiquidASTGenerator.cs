﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using System.Text.RegularExpressions;

using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

using Liquid.NET.Constants;
using Liquid.NET.Expressions;
using Liquid.NET.Grammar;
using Liquid.NET.Symbols;
using Liquid.NET.Tags;
using Liquid.NET.Utils;

namespace Liquid.NET
{

    /// <summary>
    /// When a tag or object/filter expression chain is encountered, a new AST node will be added to the AST tree.
    /// An AST node that is added to the tree will be parsed by the visitor.
    /// 
    /// An AST node may be kept out of the tree if it is to be rendered later, e.g. the blocks inside an IF/Then/Else
    /// statement.  THese may be saved within the Tag or Block object.
    /// 
    /// An object expression is an object with a series of filters after it.  This is usually found in a node of a tree structure.
    /// </summary>

    public class LiquidASTGenerator : LiquidBaseListener
    {
        // TODO: WHen done it shoudl check if all the stacks are reset.

        private BufferedTokenStream _tokenStream;
        private TokenStreamRewriter _tokenStreamRewriter;

        /// <summary>
        /// A workspace to construct the current AST Node, e.g. If/Else, etc.
        /// </summary>
        private readonly Stack<BlockBuilderContext> _blockBuilderContextStack = new Stack<BlockBuilderContext>();

        /// <summary>
        /// Keep track of where we're appending children to the AST.
        /// </summary>
        private readonly Stack<TreeNode<IASTNode>> _astNodeStack = new Stack<TreeNode<IASTNode>>();

        public LiquidAST Generate(String template)
        {
            //Console.WriteLine("Parsing Template \r\n" + template);

            //BufferedTokenStream tokenStream
            LiquidAST liquidAst = new LiquidAST();
            _astNodeStack.Push(liquidAst.RootNode);
            var stringReader = new StringReader(template);
            
            _tokenStream = new CommonTokenStream(new LiquidLexer(new AntlrInputStream(stringReader)));
            _tokenStreamRewriter = new TokenStreamRewriter(_tokenStream);

            var parser = new LiquidParser(_tokenStream);
            
            new ParseTreeWalker().Walk(this, parser.init());

            return liquidAst;
        }

        public override void EnterTag(LiquidParser.TagContext tagContext)
        {
            base.EnterTag(tagContext);
            // Console.WriteLine("CREATING TAG *" + tagContext.GetText() + "*");            
        }

        public override void EnterRaw_tag(LiquidParser.Raw_tagContext context)
        {
            base.EnterRaw_tag(context);
            _tokenStreamRewriter.Delete(context.Start);
            _tokenStreamRewriter.Delete(context.Stop);
            String txt = TrimRawTags(context.RAW().GetText());
            
            //String allTokens = _tokenStream.GetText();
            //Console.WriteLine(" *** Receiving Raw Text *** " + txt);
            var rawTag = new RawBlockTag(txt);
            var newNode = CreateTreeNode<IASTNode>(rawTag);

            CurrentAstNode.AddChild(newNode);
        }

        /// <summary>
        /// Todo: see if this can be lexed out.
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public String TrimRawTags(String str)
        {
            var str1 = Regex.Replace(str, "^{%\\s*raw\\s%}", "", RegexOptions.IgnoreCase);
            var str2 = Regex.Replace(str1, "{%\\s*endraw\\s%}$", "", RegexOptions.IgnoreCase);
            return str2;
        }
        
        public override void EnterAssign_tag(LiquidParser.Assign_tagContext context)
        {
            base.EnterAssign_tag(context);
            var assignTag = new AssignTag
            {
                VarName = context.LABEL().GetText()
            };
            //assignTag.

            var newNode = CreateTreeNode<IASTNode>(assignTag);
            //CurrentBuilderContext.AssignTag = assignTag;
            CurrentAstNode.AddChild(newNode);

            StartNewLiquidExpressionTree(result =>
            {
                //Console.WriteLine("Setting ExpRESSION TREE TO " + result);
                assignTag.LiquidExpressionTree = result;
            });
            //if (context.outputexpression() != null)
            //StartCapturingVariable(context.outputexpression().);


        }


        public override void ExitAssign_tag(LiquidParser.Assign_tagContext context)
        {
            base.ExitAssign_tag(context);

            FinishLiquidExpressionTree();
        }

        public override void EnterCapture_tag(LiquidParser.Capture_tagContext contentContext)
        {
            base.EnterCapture_tag(contentContext);
            var captureBlock = new CaptureBlockTag()
            {
                VarName = contentContext.LABEL().GetText()
            };
            var newNode = CreateTreeNode<IASTNode>(captureBlock);
            CurrentAstNode.AddChild(newNode);
            _astNodeStack.Push(captureBlock.RootContentNode);
        }

        public override void ExitCapture_block(LiquidParser.Capture_blockContext context)
        {
            base.ExitCapture_block(context);
            _astNodeStack.Pop();
        }

        #region For Tag

        public override void EnterFor_tag(LiquidParser.For_tagContext context)
        {
            base.EnterFor_tag(context);
            //Console.WriteLine("Entering FOR tag");

            var forBlock = new ForBlockTag
            {
                LocalVariable = context.for_label().LABEL().ToString()
            };
            AddNodeToAST(forBlock);

            // put the block we're currently configuring on the "for block" stack.
            CurrentBuilderContext.ForBlockStack.Push(forBlock);

            // subsequent parsing sends blocks to the root content node (i.e. the stuff to repeat)
            _astNodeStack.Push(forBlock.LiquidBlock);
                
        }


        public override void ExitFor_tag(LiquidParser.For_tagContext forContext)
        {
            //Console.WriteLine("@@@ EXITING FOR TAG *" + forContext.GetText() + "*");

            base.ExitFor_tag(forContext);
            _astNodeStack.Pop(); // stop capturing the block inside the for tag.

        }


        /// <summary>
        /// Put the parameters on the current for block in the "for block" stack.
        /// </summary>
        /// <param name="context"></param>
        public override void EnterFor_params(LiquidParser.For_paramsContext context)
        {
            base.EnterFor_params(context);
            var forBlock = CurrentBuilderContext.ForBlockStack.Peek();

            if (context.PARAM_REVERSED() != null)
            {
                forBlock.Reversed = new BooleanValue(true);
            }
            if (context.for_param_limit() != null)
            {
                forBlock.Limit = CreateIntNumericValueFromString(context.for_param_limit().NUMBER().ToString());
            }
            if (context.for_param_offset() != null)
            {
                forBlock.Offset = CreateIntNumericValueFromString(context.for_param_offset().NUMBER().ToString());
            }
        }

        /// <summary>
        /// Put the iterable (string, array, generator aka range, etc.) into the current block of the "for block" stack.
        /// </summary>
        /// <param name="context"></param>
        public override void EnterFor_iterable(LiquidParser.For_iterableContext context)
        {
            //Console.WriteLine("  ^^^ PARSING FOR ITERABLE");
            base.EnterFor_iterable(context);
            var forBlock = CurrentBuilderContext.ForBlockStack.Peek();

            // the iterators are going to be created by the visitor
            if (context.STRING() != null)
            {
                //Console.WriteLine("  +++ FOUND a STRING ");
                forBlock.IterableCreator =
                    new StringValueIterableCreator(GenerateStringSymbol(context.STRING().GetText()));
            }
            else if (context.variable() != null)
            {
                //Console.WriteLine("  +++ FOUND a VARIABLE ");
               
                StartNewLiquidExpressionTree(result =>
                {
                    //Console.WriteLine("   --- Setting ExpRESSION TREE TO " + result);
                    forBlock.IterableCreator = new ArrayValueCreator(result);
                    
                });
                StartCapturingVariable(context.variable()); // marked complete in ExitFor_iterable.
      
                
            }
            else if (context.generator() != null)
            {
                //Console.WriteLine("  +++ FOUND a GENERATOR ");
               
                forBlock.IterableCreator = CreateGeneratorContext(context.generator());
            }
            else
            {   
                //Console.WriteLine("TODO: Process the missing iterable");
                // TODO: Maybe put an UNDEFINED variable in the AST?  Or an Erroneous If?
            }
        }

        public override void ExitFor_iterable(LiquidParser.For_iterableContext context)
        {
            //Console.WriteLine("  ^^^ DONE FOR ITERABLE");
            base.ExitFor_iterable(context);

            if (context.variable() != null )
            {
                //Console.WriteLine("CALLING FINISH OBJ"); 
                //FinishLiquidExpressionTree();
                MarkCurrentExpressionComplete();
            }
        }

            private GeneratorCreator CreateGeneratorContext(LiquidParser.GeneratorContext generatorContext)
            {
                Console.WriteLine("CREATING GENERATOR");
                TreeNode<LiquidExpression> startExpression= null;
                TreeNode<LiquidExpression> endExpression = null;
               

                if (generatorContext.NUMBER(0) != null) // lower range
                {
                    startExpression =
                        CreateObjectSimpleExpressionNode(
                            CreateIntNumericValueFromString(generatorContext.NUMBER(0).GetText()));

                }
                else if (generatorContext.variable(0) != null)
                {
                    StartNewLiquidExpressionTree(x => startExpression = x);
                    StartCapturingVariable(generatorContext.variable(0));
                    MarkCurrentExpressionComplete();
                }


                if (generatorContext.NUMBER(1) != null) // lower range
                {
                    endExpression = CreateObjectSimpleExpressionNode(
                        CreateIntNumericValueFromString(generatorContext.NUMBER(1).GetText()));
                }
                else if (generatorContext.variable(1) != null)
                {
                    StartNewLiquidExpressionTree(x => endExpression = x);
                    StartCapturingVariable(generatorContext.variable(1));
                    MarkCurrentExpressionComplete();
                }


                return new GeneratorCreator(startExpression, endExpression);

                //return new GeneratorValue();
                //return CreateObjectSimpleExpressionNode(new StringValue("TODO: FIX THE GENERATOR"));
            }
                   
        #endregion

        #region Cycle Tag

        public override void EnterCycle_tag(LiquidParser.Cycle_tagContext context)
        {
            base.EnterCycle_tag(context);
            
            var cycleTag = new CycleTag
            {                
                // TODO: Allow variables in cycle?
                CycleList = context.cycle_string().Select(str => (IExpressionConstant) GenerateStringSymbol(str.GetText())).ToList()
            };
            if (context.cycle_group() != null)
            {
                cycleTag.Group = context.cycle_group().STRING().GetText();
            }

            CurrentAstNode.AddChild(CreateTreeNode<IASTNode>(cycleTag));
            //_astNodeStack.Push();
        }

        #endregion


        public override void EnterCustom_tag(LiquidParser.Custom_tagContext customContext)
        {
            base.EnterCustom_tag(customContext);

            Console.WriteLine("I see CUSTOM TAG " + customContext.tagname().GetText());
            var customTag = new CustomTag(customContext.tagname().GetText());
            AddNodeToAST(customTag);

            CurrentBuilderContext.CustomTagStack.Push(customTag);
            //_astNodeStack.Push(customBlock.LiquidBlock); // capture the block
        }

        public override void EnterCustom_blocktag(LiquidParser.Custom_blocktagContext customBlockContext)
        {

            base.EnterCustom_blocktag(customBlockContext);

            Console.WriteLine("I see CUSTOM BLOCK TAG " + customBlockContext);
            var customTag = new CustomBlockTag("test");
            AddNodeToAST(customTag);
            Console.WriteLine("START LABEL IS " + customBlockContext.LABEL());
            Console.WriteLine("END LABEL IS " + customBlockContext.ENDLABEL());
            CurrentBuilderContext.CustomBlockTagStack.Push(customTag);
            //_astNodeStack.Push(customBlock.LiquidBlock); // capture the block
        }

        public override void EnterCustomtagblock_expr(LiquidParser.Customtagblock_exprContext context)
        {
            base.EnterCustomtagblock_expr(context);
            context.outputexpression();
            var y= context.children.Select(x => x.GetText());
            Console.WriteLine("BLOCK EXPR IS " + context.outputexpression().GetText());


            StartNewLiquidExpressionTree(result =>
            {
                CurrentBuilderContext.CustomBlockTagStack.Peek().LiquidExpressionTrees.Add(result);
            });
        }

        public override void EnterCustom_blocktag_block(LiquidParser.Custom_blocktag_blockContext context)
        {
            base.EnterCustom_blocktag_block(context);
        }

        public override void EnterCustomtag_expr(LiquidParser.Customtag_exprContext context)
        {
            base.EnterCustomtag_expr(context);
            Console.WriteLine("EXPR IS "+context.outputexpression().GetText());
            

            StartNewLiquidExpressionTree(result =>
            {
                Console.WriteLine("Setting ExpRESSION TREE TO " + result);
                CurrentBuilderContext.CustomTagStack.Peek().LiquidExpressionTrees.Add(result);
                //Console.WriteLine("Setting ExpRESSION TREE TO " + result);
                //customBlock.LiquidExpressionTrees = result;
            });
        }

        public override void ExitCustomtag_expr(LiquidParser.Customtag_exprContext context)
        {
            base.ExitCustomtag_expr(context);
        }

        public override void ExitCustom_tag(LiquidParser.Custom_tagContext context)
        {
            base.ExitCustom_tag(context);
        }

        public override void EnterContinue_tag(LiquidParser.Continue_tagContext context)
        {
            base.EnterContinue_tag(context);
            ContinueTag continueTag = new ContinueTag();
            CurrentAstNode.AddChild(CreateTreeNode<IASTNode>(continueTag));
        }

        public override void EnterBreak_tag(LiquidParser.Break_tagContext context)
        {
            BreakTag breakTag = new BreakTag();
            CurrentAstNode.AddChild(CreateTreeNode<IASTNode>(breakTag));


        }


//        public override void EnterCycle_tag(LiquidParser.Cycle_tagContext context)
//        {
//            base.EnterCycle_tag(context);
//
//            var cycleTag = new CycleTag
//            {
//                // TODO: Allow variables in cycle?
//                CycleList = context.cycle_string().Select(str => (IExpressionConstant)GenerateStringSymbol(str.GetText())).ToList()
//            };
//            if (context.cycle_group() != null)
//            {
//                cycleTag.Group = context.cycle_group().STRING().GetText();
//            }
//
//            CurrentAstNode.AddChild(CreateTreeNode<IASTNode>(cycleTag));
//            //_astNodeStack.Push();
//        }


        #region If|Unless / Elsif / Else / Endif Tag

        public override void EnterUnless_tag(LiquidParser.Unless_tagContext unlessContext)
        {
            base.EnterUnless_tag(unlessContext);
            //Console.WriteLine("CREATING UNLESS TAG *" + unlessContext.GetText() + "*");
            // create the parent if/then/else container and put it in the tree
            AddIfThenElseTagToCurrent();

            // set up the "if" clause of the if/then/else container
            InitiateIfClause();

        }

        public override void ExitUnless_tag(LiquidParser.Unless_tagContext context)
        {
            base.ExitUnless_tag(context);          
            var unlessBlock = CurrentBuilderContext.IfThenElseBlockStack.Pop();

            LiquidExpression liquidExpression = new LiquidExpression { Expression = new NotExpression() };
            var newRoot = new TreeNode<LiquidExpression>(liquidExpression);
            newRoot.AddChild(unlessBlock.IfElseClauses[0].LiquidExpressionTree);
            unlessBlock.IfElseClauses[0].LiquidExpressionTree = newRoot;
            EndIfClause();
            
        }

        public override void EnterIf_tag(LiquidParser.If_tagContext ifContext)
        {
            base.EnterIf_tag(ifContext);
            //Console.WriteLine("CREATING IF TAG *" + ifContext.GetText() + "*");

            // create the parent if/then/else container and put it in the tree
            AddIfThenElseTagToCurrent();

            // set up the "if" clause of the if/then/else container
            InitiateIfClause();
        }

        /// <summary>
        /// Create the representation of the if/then/else tag on the stack.
        /// </summary>
        private void AddIfThenElseTagToCurrent()
        {
            //Console.WriteLine("Adding If then else to current");
            //CurrentBuilderContext.IfThenElseBlockTag = new IfThenElseBlockTag();
            var ifThenElseBlock = new IfThenElseBlockTag();
            //Console.WriteLine("  -:> Pushing if block on stack");
            CurrentBuilderContext.IfThenElseBlockStack.Push(ifThenElseBlock);
            var newNode = CreateTreeNode<IASTNode>(ifThenElseBlock);
            CurrentAstNode.AddChild(newNode);
        }


        /// <summary>
        /// Create an "if" clause and put it on the stack.
        /// </summary>
        private void InitiateIfClause()
        {
            var elsIfSymbol = new IfElseClause();
            //Console.WriteLine("Creating if expressino");
            CurrentBuilderContext.IfThenElseBlockStack.Peek().AddIfClause(elsIfSymbol);
            _astNodeStack.Push(elsIfSymbol.LiquidBlock); // capture the block
        }



        public override void EnterIfexpr(LiquidParser.IfexprContext context)
        {
            base.EnterIfexpr(context);
            //Console.WriteLine("New Expression Builder");
            //InitiateExpressionBuilder();
            var ifexpr = CurrentBuilderContext.IfThenElseBlockStack.Peek().IfElseClauses.Last();
            StartNewLiquidExpressionTree(x => ifexpr.LiquidExpressionTree = x);
        }

        public override void ExitIfexpr(LiquidParser.IfexprContext context)
        {
            base.ExitIfexpr(context);
            //Console.WriteLine("End Expression Builder");
            
            //CurrentBuilderContext.LiquidExpressionBuilder.StartLiquidExpression();
//            CurrentBuilderContext.IfThenElseBlockStack.Peek().IfElseClauses.Last().LiquidExpressionTree =
//                CurrentBuilderContext.LiquidExpressionBuilder.ConstructedLiquidExpressionTree;
                

            FinishLiquidExpressionTree();
        }


        public override void EnterElsif_tag(LiquidParser.Elsif_tagContext elsIfContext)
        {
            base.EnterElsif_tag(elsIfContext);
            //Console.WriteLine("CREATING ELSIF TAG *" + elsIfContext.GetText() + "*");
            InitiateIfClause();
        }

        public override void EnterElse_tag(LiquidParser.Else_tagContext elseContext)
        {
            base.EnterElse_tag(elseContext);
            InitiateIfClause();

            //ExpressionBuilder expressionBuilder = new ExpressionBuilder();
            var symbol = new BooleanValue(true);
            CurrentBuilderContext.IfThenElseBlockStack.Peek()
                .IfElseClauses.Last()
                .LiquidExpressionTree = CreateObjectSimpleExpressionNode(symbol);

        }

        public override void ExitIf_tag(LiquidParser.If_tagContext ifContext)
        {
            base.EnterIf_tag(ifContext);
            //Console.WriteLine("  <:- Popping if block off stack");
            CurrentBuilderContext.IfThenElseBlockStack.Pop();

            //Console.WriteLine("EXITING IF/ELSE/ELSIF TAG *" + ifContext.GetText() + "*");
            EndIfClause();
        }

        public override void ExitElsif_tag(LiquidParser.Elsif_tagContext elsIfContext)
        {
            base.ExitElsif_tag(elsIfContext);
            //Console.WriteLine("EXITING ELSIF TAG *" + elsIfContext.GetText() + "*");
            EndIfClause();
        }

        public override void ExitElse_tag(LiquidParser.Else_tagContext elseContext)
        {
            base.ExitElse_tag(elseContext);
            EndIfClause();
            //CurrentBuilderContext.IfThenElseBlockTag.ElseSymbol = //CurrentBuilderContext.ExpressionBuilder.ConstructedExpression;
        }


        private void EndIfClause()
        {
            _astNodeStack.Pop();
        }

        public override void EnterIncrement_tag(LiquidParser.Increment_tagContext incrementContext)
        {
            base.EnterIncrement_tag(incrementContext);
            var incrementTag = new IncrementTag
            {
                VarName = incrementContext.LABEL().GetText()
            };
           
            var newNode = CreateTreeNode<IASTNode>(incrementTag);
            CurrentAstNode.AddChild(newNode);
        }

        public override void EnterDecrement_tag(LiquidParser.Decrement_tagContext decrementContext)
        {
            base.EnterDecrement_tag(decrementContext);
            var decrementTag = new DecrementTag()
            {
                VarName = decrementContext.LABEL().GetText()
            };

            var newNode = CreateTreeNode<IASTNode>(decrementTag);
            CurrentAstNode.AddChild(newNode);
        }

        private void StartNewLiquidExpressionTree(Action<TreeNode<LiquidExpression>> setExpression)
        {
            CurrentBuilderContext.LiquidExpressionBuilder = new LiquidExpressionTreeBuilder();
            
            CurrentBuilderContext.LiquidExpressionBuilder.ExpressionCompleteEvent += new OnExpressionCompleteEventHandler(setExpression);
        }

        private void FinishLiquidExpressionTree()
        {
            //Console.WriteLine("   <<< End FinishLiquidExpression (set Builder to Null)");

            CurrentBuilderContext.LiquidExpressionBuilder = null;
        }

        
        #endregion

        #region Case / When / Else tag

        public override void EnterCase_tag(LiquidParser.Case_tagContext context)
        {
            base.EnterCase_tag(context);
            Console.WriteLine(">>>FOUND CASE");
            var caseBlock = new CaseWhenElseBlockTag();

            CurrentBuilderContext.CaseWhenElseBlockStack.Push(caseBlock);
            var newNode = CreateTreeNode<IASTNode>(caseBlock);
            CurrentAstNode.AddChild(newNode);
            
            // TODO: probably the eval-ed value to compare can 
            // be "Equalled" with the when-expression and
            // so converted to a predicate.
            StartNewLiquidExpressionTree(result =>
            {
                Console.WriteLine("Setting Expression tree to" + result);
                caseBlock.LiquidExpressionTree = result;
            });

        }

        public override void ExitCase_tag(LiquidParser.Case_tagContext context)
        {
            base.ExitCase_tag(context);
            Console.WriteLine("<<<Exit Case Tag");
            CurrentBuilderContext.CaseWhenElseBlockStack.Pop();
            //Console.WriteLine(context.whenblock().)
            //CurrentBuilderContext.CaseWhenElseBlockStack.Pop();

            FinishLiquidExpressionTree();
        }
     
        /*
         * public override void EnterIfexpr(LiquidParser.IfexprContext context)
        {
            base.EnterIfexpr(context);
            //Console.WriteLine("New Expression Builder");
            //InitiateExpressionBuilder();
            var ifexpr = CurrentBuilderContext.IfThenElseBlockStack.Peek().IfElseClauses.Last();
            StartNewLiquidExpressionTree(x => ifexpr.LiquidExpressionTree = x);
        }

        public override void ExitIfexpr(LiquidParser.IfexprContext context)
        {
            base.ExitIfexpr(context);
            //Console.WriteLine("End Expression Builder");
            
            //CurrentBuilderContext.LiquidExpressionBuilder.StartLiquidExpression();
//            CurrentBuilderContext.IfThenElseBlockStack.Peek().IfElseClauses.Last().LiquidExpressionTree =
//                CurrentBuilderContext.LiquidExpressionBuilder.ConstructedLiquidExpressionTree;
                

            FinishLiquidExpressionTree();
        }*/

        public override void EnterCase_tag_contents(LiquidParser.Case_tag_contentsContext context)
        {
            base.EnterCase_tag_contents(context);
            Console.WriteLine("CASE COntents");
        }

        public override void EnterWhen_tag(LiquidParser.When_tagContext context)
        {
            base.EnterWhen_tag(context);
            Console.WriteLine("When Tag");
            InitiateWhenClause();

            StartNewLiquidExpressionTree(result =>
            {
                Console.WriteLine("Setting Expression tree to" + result);
                CurrentBuilderContext.CaseWhenElseBlockStack.Peek().WhenClauses.Last().LiquidExpressionTree = result;
            });

        }

        public override void ExitWhen_tag(LiquidParser.When_tagContext context)
        {
            base.ExitWhen_tag(context);
            EndWhenClause();
        }

        public override void EnterWhenblock(LiquidParser.WhenblockContext context)
        {
            base.EnterWhenblock(context);
            Console.WriteLine("WHEN BLOCK");
        }

        public override void EnterWhen_else_tag(LiquidParser.When_else_tagContext context)
        {
            base.EnterWhen_else_tag(context);
            //InitiateIfClause();
            //InitiateWhenClause();
            var elseClause = new CaseWhenElseBlockTag.WhenElseClause();
            CurrentBuilderContext.CaseWhenElseBlockStack.Peek().ElseClause = elseClause;
            _astNodeStack.Push(elseClause.LiquidBlock); // capture the block
            //ExpressionBuilder expressionBuilder = new ExpressionBuilder();
            //var symbol = new BooleanValue(true);
//            CurrentBuilderContext.CaseWhenElseBlockStack.Peek()
//                .ElseClause
//                .LiquidExpressionTree = CreateObjectSimpleExpressionNode(symbol);
        }

        public override void ExitWhen_else_tag(LiquidParser.When_else_tagContext context)
        {
            base.ExitWhen_else_tag(context);
            Console.WriteLine("Exit When Else");
            //EndWhenClause();
            _astNodeStack.Pop();
        }

        private void InitiateWhenClause()
        {
            var whenBlock = new CaseWhenElseBlockTag.WhenClause();
            Console.WriteLine("Creating if expressino");
            CurrentBuilderContext.CaseWhenElseBlockStack.Peek().AddWhenBlock(whenBlock);
            _astNodeStack.Push(whenBlock.LiquidBlock); // capture the block
        }
        private void EndWhenClause()
        {
            _astNodeStack.Pop();
        }
        #endregion


        /// <summary>
        /// Start capturing the tree of variable references and indices, transforming them as Antlr descends the
        /// tree into a tree of LiquidExpressions.  Each LiquidExpression is a VariableReference + a potential set of filters
        /// to index it.  (The indices may contain nested LiquidExpressions, hence the tree).
        /// 
        /// The result will be at CurrentBuilderContext.LiquidExpressionBuilder.ConstructedLiquidExpressionTree.
        /// </summary>
        /// <param name="variableContext"></param>
        private void StartCapturingVariable(LiquidParser.VariableContext variableContext)
        {
            //Console.WriteLine("START Capturing variable " + variableContext.LABEL().GetText());
            var varname = variableContext.LABEL().GetText();
            IEnumerable<FilterSymbol> indexLookupFilters =
                variableContext.objectvariableindex().Select(AddIndexLookupFilter);
            AddExpressionToCurrentExpressionBuilder(new VariableReference(varname));
            foreach (var filter in indexLookupFilters)
            {
                //Console.WriteLine("  ADDING FILTER TO VARIABLE OBJECT " + filter);
                CurrentBuilderContext.LiquidExpressionBuilder.AddFilterSymbolToCurrentExpression(filter);
            }
            //Console.WriteLine("START Capturing variable END");
        }
        /// <summary>
        /// Mark the current expression complete.  Needs to be called after AddExpressionToCurrentExpressionBuilder.
        /// </summary>
        private void MarkCurrentExpressionComplete()
        {
            //Console.WriteLine("Current expression complete!");
            CurrentBuilderContext.LiquidExpressionBuilder.EndLiquidExpression();
        }


        #region Expressions

        public override void EnterGroupedExpr(LiquidParser.GroupedExprContext context)
        {
            base.EnterGroupedExpr(context);
            AddExpressionToCurrentExpressionBuilder(new GroupedExpression());
        }

        public override void ExitGroupedExpr(LiquidParser.GroupedExprContext context)
        {
            base.ExitGroupedExpr(context);
            MarkCurrentExpressionComplete();
        }

        public override void EnterMultExpr(LiquidParser.MultExprContext multContext)
        {
            base.EnterMultExpr(multContext);
            Console.WriteLine(" === creating MULTIPLICATION expression >" + multContext.GetText() + "<");
        }

        public override void EnterAddSubExpr(LiquidParser.AddSubExprContext addContext)
        {
            base.EnterAddSubExpr(addContext);
            Console.WriteLine(" === creating ADD expression >" + addContext.GetText() + "<");
        }

        public override void EnterAndExpr(LiquidParser.AndExprContext andContext)
        {
            base.EnterAndExpr(andContext);
            AddExpressionToCurrentExpressionBuilder(new AndExpression());
            Console.WriteLine(" === creating AND expression >" + andContext.GetText() + "<");
            
        }

        public override void ExitAndExpr(LiquidParser.AndExprContext andContext)
        {
            base.ExitAndExpr(andContext);
            Console.WriteLine(" --- exiting AND expression >" + andContext.GetText() + "<");
            MarkCurrentExpressionComplete();
        }

        public override void EnterNotExpr(LiquidParser.NotExprContext notContext)
        {
            base.EnterNotExpr(notContext);
            AddExpressionToCurrentExpressionBuilder(new NotExpression());
            Console.WriteLine(" === creating NOT expression >" + notContext.GetText() + "<");

        }

        public override void ExitNotExpr(LiquidParser.NotExprContext notContext)
        {
            base.ExitNotExpr(notContext);
            Console.WriteLine(" --- exiting NOT expression >" + notContext.GetText() + "<");
            MarkCurrentExpressionComplete();
        }

        public override void EnterOrExpr(LiquidParser.OrExprContext orContext)
        {
            base.EnterOrExpr(orContext);          
            Console.WriteLine(" === creating OR expression >" + orContext.GetText() + "<");
            AddExpressionToCurrentExpressionBuilder(new OrExpression());
        }

        public override void ExitOrExpr(LiquidParser.OrExprContext orContext)
        {
            base.ExitOrExpr(orContext);
            Console.WriteLine(" --- exiting OR expression >" + orContext.GetText() + "<");
            MarkCurrentExpressionComplete();
        }

        public override void EnterComparisonExpr(LiquidParser.ComparisonExprContext comparisonContext)
        {
            base.EnterComparisonExpr(comparisonContext);
            Console.WriteLine(" === creating COMPARISON expression >" + comparisonContext.GetText() + "<");
            if (comparisonContext.EQ() != null)
            {
                Console.WriteLine(" +++ EQUALS");
                AddExpressionToCurrentExpressionBuilder(new EqualsExpression());
            }
            else if (comparisonContext.GT() != null)
            {
                Console.WriteLine(" +++ GT");
                AddExpressionToCurrentExpressionBuilder(new GreaterThanExpression());
            }
            else if (comparisonContext.LT() != null)
            {
                Console.WriteLine(" +++ LT");
                AddExpressionToCurrentExpressionBuilder(new LessThanExpression());
            }
            else if (comparisonContext.LTE() != null)
            {
                AddExpressionToCurrentExpressionBuilder(new LessThanOrEqualsExpression());
            }
            else if (comparisonContext.GTE() != null)
            {
                AddExpressionToCurrentExpressionBuilder(new GreaterThanOrEqualsExpression());
            }
            else
            {
                // TODO: figure out what to return here
                throw new Exception("Invalid comparison: "+ comparisonContext.GetText()); 
            }
        }

        public override void ExitComparisonExpr(LiquidParser.ComparisonExprContext context)
        {
            base.ExitComparisonExpr(context);
            Console.WriteLine(" --- exiting COMPARISON expression >" + context.GetText() + "<");
            MarkCurrentExpressionComplete();
        }

        // todo: rename this "Object" or something to indicate it's just teh Object part of the expression.
        public override void EnterOutputExpression(LiquidParser.OutputExpressionContext context)
        {
            Console.WriteLine("))) Entering Output Expression!  Expression creation should follow.");
            base.EnterOutputExpression(context);

        }


        public override void ExitOutputExpression(LiquidParser.OutputExpressionContext context)
        {
            base.ExitOutputExpression(context);
            
            Console.WriteLine("Done CREATING OUTPUT EXPRESSION" + context.GetText() + "<");
            //MarkCurrentExpressionComplete();
        }
        

        /// <summary>
        /// record a new expression
        /// </summary>
        private void AddExpressionToCurrentExpressionBuilder(IExpressionDescription symbol)
        {
            Console.WriteLine("AddExpressionToCurrentExpressionBuilder "+symbol);
            CurrentBuilderContext.LiquidExpressionBuilder.StartLiquidExpression(symbol);
        }

        private static TreeNode<LiquidExpression> CreateObjectSimpleExpressionNode(IExpressionDescription expressionDescription)
        {
            return new TreeNode<LiquidExpression>(new LiquidExpression { Expression = expressionDescription });
        }



  

        #endregion

        public override void EnterBlock(LiquidParser.BlockContext blockContext)
        {
            _blockBuilderContextStack.Push(new BlockBuilderContext());
            base.EnterBlock(blockContext);
            //Console.WriteLine(">>> ENTERING BLOCK *" + blockContext.GetText() + "*");
        }

        public override void ExitBlock(LiquidParser.BlockContext blockContext)
        {
            _blockBuilderContextStack.Pop();
            base.ExitBlock(blockContext);
            //Console.WriteLine(">>> EXITING BLOCK *" + blockContext.GetText() + "*");
        }

        #region Output / Filter

        public override void EnterStringObject(LiquidParser.StringObjectContext context)
        {
            //Console.WriteLine("CREATING STRING OBJECT" + context.GetText() + "<");
            // TODO: Figure out how to strip the quotes in the g4 file
            base.EnterStringObject(context);
         
            AddExpressionToCurrentExpressionBuilder(GenerateStringSymbol(context.GetText()));
        }

        public override void ExitStringObject(LiquidParser.StringObjectContext context)
        {
            base.ExitStringObject(context);
            MarkCurrentExpressionComplete();
        }

        /// <summary>
        /// TODO: Strip the quotes in the parser/lexer.  Until then, we'll do it here.
        /// </summary>
        private StringValue GenerateStringSymbol(String text)
        {
            return new StringValue(StripQuotes(text));
            //return new StringValue(text);
        }

        private string StripQuotes(String str)
        {
            return str.Substring(1, str.Length - 2);
        }
        //override Ge

        public override void EnterVariableObject(LiquidParser.VariableObjectContext context)
        {
            base.EnterVariableObject(context);

            Console.WriteLine("<><> Entering VariableReference " + context.GetText());
            //StartCapturingVariable(context.variable());
            StartCapturingVariable(context.variable());
//            InitiateVariableWithIndex(
//                context.LABEL().GetText(),
//                context.objectvariableindex().Select(AddIndexLookupFilter));
        }

        public override void ExitVariableObject(LiquidParser.VariableObjectContext context)
        {
            base.ExitVariableObject(context);
            MarkCurrentExpressionComplete();
        }

        // TODO: clean this up
        private static FilterSymbol AddIndexLookupFilter(LiquidParser.ObjectvariableindexContext objectvariableindexContext)
        {
            Console.WriteLine("Working on index filter.");
            var indexingFilter = new FilterSymbol("lookup"); // TODO: Should this be in a separate namespace or something?

            if (objectvariableindexContext.objectproperty() != null)
            {
                var index = objectvariableindexContext.objectproperty().GetText();
                if (index != null)
                {
                    indexingFilter.AddArg(new StringValue(index.TrimStart('.'))); // todo: make the lexing take care of the "."
                    return indexingFilter;
                }
            }
            
            var arrayIndex = objectvariableindexContext.arrayindex();
            if (arrayIndex != null)
            {
                if (arrayIndex.ARRAYINT() != null)
                {
                    Console.WriteLine("=== Array Index is " + arrayIndex.ARRAYINT().GetText());
                    indexingFilter.AddArg(CreateIntNumericValueFromString(arrayIndex.ARRAYINT().GetText()));
                    return indexingFilter;
                }
                if (arrayIndex.STRING() != null)
                {
                    indexingFilter.AddArg(new StringValue(arrayIndex.STRING().GetText()));
                    return indexingFilter;
                }
                // TODO: Rewrite this using "variable"

//                if (arrayIndex.variable() != null)
//                {
//                    StartCapturingVariable(arrayIndex.variable());
//
//                    var expression = CurrentBuilderContext.LiquidExpressionBuilder.ConstructedLiquidExpressionTree;
//                    var refChain = new ObjectReferenceChain(expression.Data); // TODO: Check if this is right?
//                    indexingFilter.AddArg(refChain);
//                    //arrayIndex.objectvariableindex();
//
//                    return indexingFilter;
//
//                }
                if (arrayIndex.LABEL() != null)
                {


                    // maybe this shoud be a wrapper instead of a chain
                    var arrayIndexLiquidExpression = new LiquidExpression
                    {
                        Expression = new VariableReference(arrayIndex.LABEL().GetText())
                    };

                    // todo: switch tho AddFilterSymbols
                    foreach (var filter in arrayIndex.objectvariableindex().Select(AddIndexLookupFilter))
                    { 
                        arrayIndexLiquidExpression.AddFilterSymbol(filter);
                    }
                    // This chain needs to be evaluated --- somehow the parent evaluation needs
                    // to be able to pick up on it....
                    var refChain = new ObjectReferenceChain(arrayIndexLiquidExpression);
                    indexingFilter.AddArg(refChain);
                    //arrayIndex.objectvariableindex();
                    
                    return indexingFilter;
                }

            }

            throw new Exception("There is a problem in the parser: the indexing is incorrect.");
        }

        private static NumericValue CreateIntNumericValueFromString(string intstring)
        {
            return new NumericValue(Convert.ToInt32(intstring));
        }

        public override void EnterNumberObject(LiquidParser.NumberObjectContext context)
        {
            Console.WriteLine("CREATING NUMBER OBJECT  >" + context.GetText() + "<");
            base.EnterNumberObject(context);
            AddExpressionToCurrentExpressionBuilder(NumericValue.Parse(context.GetText()));


        }

        public override void ExitNumberObject(LiquidParser.NumberObjectContext context)
        {
            base.ExitNumberObject(context);
            MarkCurrentExpressionComplete();
        }


        public override void EnterBooleanObject(LiquidParser.BooleanObjectContext context)
        {
            Console.WriteLine("CREATING Boolean OBJECT >" + context.GetText() + "<");
            base.EnterBooleanObject(context);
            //zzz

            //CurrentBuilderContext.ExpressionBuilder.AddExpression(symbol);

            AddExpressionToCurrentExpressionBuilder(new BooleanValue(Convert.ToBoolean(context.GetText())));
        }

        public override void ExitBooleanObject(LiquidParser.BooleanObjectContext context)
        {
            base.ExitBooleanObject(context);
            MarkCurrentExpressionComplete();
        }

        /// <summary>
        /// Enter the {{ ... }} filter, and delete the "{{" and the "}}" tokens.
        /// </summary>
        /// <param name="context"></param>
        public override void EnterOutputmarkup(LiquidParser.OutputmarkupContext context)
        {
            base.EnterOutputmarkup(context);
            Console.WriteLine("->-ENTERING OUTPUT MARKUP ");
            //_liquidAst.AddChild();
            _tokenStreamRewriter.Delete(context.Start); // Delete the opening // TODO: I don't think these are necessary now that we're not using the token stream
            _tokenStreamRewriter.Delete(context.Stop); // and closing braces
            StartNewLiquidExpressionTree(x => CurrentAstNode.AddChild(CreateTreeNode<IASTNode>(
                new LiquidExpressionTree(x))));

        }

        public override void ExitOutputmarkup(LiquidParser.OutputmarkupContext context)
        {
            base.ExitOutputmarkup(context);
            //CurrentBuilderContext.IfThenElseBlockTag.IfElseClauses.Last().LiquidExpression =
            Console.WriteLine("-<-EXITING OUTPUT dMARKUP ");

            // TODO: the parser can create a composite expression here, but the output markup only ever sends
            // a simple object expression in.  I think _currentLiquidExpression.Expression should be a tree
            // anyway, rather than just assuming that the first expression (i.e. we're taking ".Data" of the top
            // node) is the only one.

            FinishLiquidExpressionTree();
        }


        /// <summary>
        /// Save the filter reference
        /// </summary>
        /// <param name="context"></param>
        public override void EnterFiltername(LiquidParser.FilternameContext context)
        {
            base.EnterFiltername(context);
            //Console.WriteLine("CREATING FILTER " + context.GetText());

            
            //_currentFilterSymbol = new FilterSymbol(context.GetText());
            CurrentBuilderContext.LiquidExpressionBuilder.AddFilterSymbolToLastExpression(new FilterSymbol(context.GetText()));

        }

        public override void EnterOutputexpression(LiquidParser.OutputexpressionContext context)
        {
            //Console.WriteLine("* Entering Output Expression");
            base.EnterOutputexpression(context);
            //StartNewLiquidExpressionTree();
            
        }

        public override void ExitOutputexpression(LiquidParser.OutputexpressionContext context)
        {
            //Console.WriteLine("* Exiting Output Expression");
            base.ExitOutputexpression(context);            
            //FinishLiquidExpressionTree();
        }

        public override void EnterStringFilterArg(LiquidParser.StringFilterArgContext context)
        {
            base.EnterStringFilterArg(context);
            Console.WriteLine("Enter STRING FILTERARG " + context.GetText());
            CurrentBuilderContext.LiquidExpressionBuilder.AddFilterArgToLastExpressionsFilter(
                GenerateStringSymbol(context.GetText()));
        }

        public override void EnterNumberFilterArg(LiquidParser.NumberFilterArgContext context)
        {
            base.EnterNumberFilterArg(context);
            Console.WriteLine("Enter NUMBER FILTERARG " + context.GetText());
            CurrentBuilderContext.LiquidExpressionBuilder.AddFilterArgToLastExpressionsFilter(
                NumericValue.Parse(context.GetText()));

        }

        public override void EnterBooleanFilterArg(LiquidParser.BooleanFilterArgContext context)
        {
            base.EnterBooleanFilterArg(context);
            Console.WriteLine("Enter BOOLEAN FILTERARG " + context.GetText());
            CurrentBuilderContext.LiquidExpressionBuilder.AddFilterArgToLastExpressionsFilter(
                new BooleanValue(Convert.ToBoolean(context.GetText())));
        }

        public override void EnterVariableFilterArg(LiquidParser.VariableFilterArgContext context)
        {
            base.EnterVariableFilterArg(context);
            Console.WriteLine("Enter VARIABLE FILTERARG " + context.GetText());
            CurrentBuilderContext.LiquidExpressionBuilder.AddFilterArgToLastExpressionsFilter(
                new VariableReference(context.GetText()));
        }

        // TODO: Add all the filterarg types.

        /// <summary>
        /// Save the raw filter argument string.  Liquid says that a filter has
        /// one argument, so this is it.
        /// </summary>
        /// <param name="context"></param>
        public override void EnterFilterargs(LiquidParser.FilterargsContext context)
        {
            base.EnterFilterargs(context);
            var originalTokens = _tokenStream.GetTokens(context.Start.TokenIndex, context.Stop.TokenIndex);
            // This removes extra whitespace---which may not be what we want...?
            // May need to figure out how to put the whitespace in the hidden stream for this only.
            String normalizedArgs = String.Join(" ", originalTokens.Select(x => x.Text));
            //Console.WriteLine("Saving raw args " + normalizedArgs);

            CurrentBuilderContext.LiquidExpressionBuilder.SetRawArgsForLastExpressionsFilter(normalizedArgs);
        }

        #endregion

        public override void EnterRawtext(LiquidParser.RawtextContext context)
        {
            base.EnterRawtext(context);
            //Console.WriteLine("ADDING TEXT :"+context.GetText());
            CurrentAstNode.AddChild(CreateTreeNode<IASTNode>(new RawBlockTag(context.GetText())));
        }


        private static TreeNode<T> CreateTreeNode<T>(T data )
        {
            return new TreeNode<T>(data);
        }

        private TreeNode<IASTNode> CurrentAstNode
        {
            get { return _astNodeStack.Peek(); }
        }

        private BlockBuilderContext CurrentBuilderContext 
        {
            get { return _blockBuilderContextStack.Peek();  }
        }

        private void AddNodeToAST(IASTNode node)
        {
            var newNode = CreateTreeNode<IASTNode>(node);
            CurrentAstNode.AddChild(newNode);
        }

        private class BlockBuilderContext
        {
            //public CurrentObjectFilterExpression 
            public readonly Stack<CustomTag> CustomTagStack = new Stack<CustomTag>();
            public readonly Stack<CustomBlockTag> CustomBlockTagStack = new Stack<CustomBlockTag>();
            public readonly Stack<IfThenElseBlockTag> IfThenElseBlockStack = new Stack<IfThenElseBlockTag>();
            public readonly Stack<CaseWhenElseBlockTag> CaseWhenElseBlockStack = new Stack<CaseWhenElseBlockTag>();
            //public ExpressionBuilder ExpressionBuilder { get; set; }
            public LiquidExpressionTreeBuilder LiquidExpressionBuilder { get; set; }
            public readonly Stack<ForBlockTag> ForBlockStack = new Stack<ForBlockTag>();
        }


       
       
    }

    
}
