﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
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

    public class LiquidASTGenerator : LiquidBaseListener, ILiquidASTGenerator
    {
        // TODO: WHen done it shoudl check if all the stacks are reset.
        private int _level = 0;

        public event OnParsingErrorEventHandler ParsingErrorEventHandler;

        private BufferedTokenStream _tokenStream;
        private TokenStreamRewriter _tokenStreamRewriter;

        private readonly IList<LiquidError> _liquidErrors = new List<LiquidError>();
        public IList<LiquidError> LiquidErrors { get { return _liquidErrors;  } }

        /// <summary>
        /// A workspace to construct the current AST Node, e.g. If/Else, etc.
        /// </summary>
        private readonly Stack<BlockBuilderContext> _blockBuilderContextStack = new Stack<BlockBuilderContext>();

        private readonly Stack<Tuple<CustomTag, TreeNode<IASTNode>>> _customTagStackAndParent= new Stack<Tuple<CustomTag, TreeNode<IASTNode>>>();

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

            var liquidLexer = new LiquidLexer(new AntlrInputStream(stringReader));

            _tokenStream = new CommonTokenStream(liquidLexer);
            _tokenStreamRewriter = new TokenStreamRewriter(_tokenStream);
            
            var parser = new LiquidParser(_tokenStream);
        
            // This is a two-stage parse SSL-first to prevent the slowness of the
            // adaptive prediction, see:
            // https://github.com/antlr/antlr4/issues/192#issuecomment-15238595
            // https://theantlrguy.atlassian.net/wiki/pages/viewpage.action?pageId=1900591
            // http://www.antlr.org/papers/allstar-techreport.pdf (section 3.2)
            var defaultErrorStrategy = parser.ErrorHandler;
            try
            {
                parser.ErrorHandler = new BailErrorStrategy();
                parser.Interpreter.PredictionMode = PredictionMode.Sll;
                parser.AddErrorListener(CreateLiquidErrorListener());
                new ParseTreeWalker().Walk(this, parser.init());
            }
            catch
            {
                parser.Reset();
                parser.ErrorHandler = defaultErrorStrategy;
                parser.Interpreter.PredictionMode = PredictionMode.Ll;
                // the error listener is still listening from the try block...
                //parser.AddErrorListener(CreateLiquidErrorListener());
                new ParseTreeWalker().Walk(this, parser.init());
            }

            // START DEBUG
            //parser.RemoveErrorListeners();
            //parser.AddErrorListener(new DiagnosticErrorListener());
            //parser.Interpreter.PredictionMode = PredictionMode.LlExactAmbigDetection;           
            // END DEBUG 
            
            if (LiquidErrors.Any())
            {
                throw new LiquidParserException(LiquidErrors);
            }

            return liquidAst;
        }

        private LiquidErrorListener CreateLiquidErrorListener()
        {
            var liquidErrorListener = new LiquidErrorListener();
            liquidErrorListener.ParsingErrorEventHandler += ParsingErrorEventHandler;
            liquidErrorListener.ParsingErrorEventHandler += ErrorHandler;
            return liquidErrorListener;
        }

        /// <summary>
        /// For debugging, verify that all the stacks have been cleaned up.
        /// </summary>
        /// <returns></returns>
        public IList<String> GetNonEmptyStackErrors()
        {
            return this.CurrentBuilderContext.VerifyStacksEmpty();
        }

        public override void EnterEveryRule(ParserRuleContext context)
        {
            base.EnterEveryRule(context);
            _level++;
        }

        public override void ExitEveryRule(ParserRuleContext context)
        {
            base.EnterEveryRule(context);
            _level--;
        }

        public override void EnterTag(LiquidParser.TagContext tagContext)
        {
            //Console.WriteLine("# EnterTag");
            base.EnterTag(tagContext);          
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

        #region Include
        public override void EnterInclude_tag(LiquidParser.Include_tagContext context)
        {
            base.EnterInclude_tag(context);
            //Console.WriteLine("Creating an include tag");

            var includeTag = new IncludeTag();
            AddNodeToAST(includeTag);

            // put the block we're currently configuring on the "for block" stack.
            CurrentBuilderContext.IncludeTagStack.Push(includeTag);

            
        }

        public override void ExitInclude_tag(LiquidParser.Include_tagContext context)
        {
            base.ExitInclude_tag(context);
            CurrentBuilderContext.IncludeTagStack.Pop();
        }

        public override void EnterInclude_expr(LiquidParser.Include_exprContext context)
        {
            base.EnterInclude_expr(context);
            StartNewLiquidExpressionTree(result =>
            {
                //Console.WriteLine("+_+ Setting INCLUDE value ");
                CurrentBuilderContext.IncludeTagStack.Last().VirtualFileExpression = result;
            });
        }

        public override void ExitInclude_expr(LiquidParser.Include_exprContext context)
        {
            base.ExitInclude_expr(context);
            FinishLiquidExpressionTree();
        }

        public override void EnterInclude_with(LiquidParser.Include_withContext context)
        {
            base.EnterInclude_with(context);
            StartNewLiquidExpressionTree(result =>
            {
                //Console.WriteLine("Setting INCLUDE WITH");
                CurrentBuilderContext.IncludeTagStack.Last().WithExpression = result;
            });
        }

        public override void ExitInclude_with(LiquidParser.Include_withContext context)
        {
            base.ExitInclude_with(context);
            FinishLiquidExpressionTree();
        }

        public override void EnterInclude_for(LiquidParser.Include_forContext context)
        {
            base.EnterInclude_for(context);
            StartNewLiquidExpressionTree(result =>
            {
                //Console.WriteLine("Setting INCLUDE for");
                CurrentBuilderContext.IncludeTagStack.Last().ForExpression = result;
            });
        }

        public override void ExitInclude_for(LiquidParser.Include_forContext context)
        {
            base.ExitInclude_for(context);
            FinishLiquidExpressionTree();
        }

        public override void EnterInclude_param_pair(LiquidParser.Include_param_pairContext context)
        {
            base.EnterInclude_param_pair(context);
            String label = context.VARIABLENAME().GetText();
            StartNewLiquidExpressionTree(result =>
            {
                //Console.WriteLine(" ---> Setting INCLUDE for "+label + " = " + result );
                CurrentBuilderContext.IncludeTagStack.Last().Definitions.Add(label, result);
                //Console.WriteLine("THere are " + CurrentBuilderContext.IncludeTagStack.Last().Definitions.Count() +
                //                  " definitions");
            });
        }

        public override void ExitInclude_param_pair(LiquidParser.Include_param_pairContext context)
        {
            base.ExitInclude_param_pair(context);
            FinishLiquidExpressionTree();
        }

        #endregion

        public override void EnterAssign_tag(LiquidParser.Assign_tagContext context)
        {
            base.EnterAssign_tag(context);
            var label = context.VARIABLENAME();
            if (label == null)
            {
                // ignore the assignment
                return;
            }
            var assignTag = new AssignTag
            {
                VarName = label.GetText()
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
                VarName = contentContext.VARIABLENAME().GetText()
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

        //public override void Enter


        #region For Tag

        public override void EnterFor_tag(LiquidParser.For_tagContext context)
        {
            base.EnterFor_tag(context);
            //Console.WriteLine("Entering FOR tag");

            var forBlock = new ForBlockTag
            {
                LocalVariable = context.for_label().VARIABLENAME().ToString()
            };
            AddNodeToAST(forBlock);

            // put the block we're currently configuring on the "for block" stack.
            CurrentBuilderContext.ForBlockStack.Push(forBlock);

            // subsequent parsing sends blocks to the root content node (i.e. the stuff to repeat)
            _astNodeStack.Push(forBlock.LiquidBlock);
                
        }

        public override void EnterFor_else(LiquidParser.For_elseContext context)
        {
            base.EnterFor_else(context);
            var forBlock = CurrentBuilderContext.ForBlockStack.Peek();
            // capture the liquid block to "else"
            _astNodeStack.Push(forBlock.ElseBlock);
        }

        public override void ExitFor_else(LiquidParser.For_elseContext context)
        {
            base.ExitFor_else(context);
            _astNodeStack.Pop();
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
                if (context.for_param_limit().NUMBER() != null)
                {
                    forBlock.Limit = CreateObjectSimpleExpressionNode(
                        CreateIntNumericValueFromString(context.for_param_limit().NUMBER().ToString()));
                }
                else if (context.for_param_limit().variable() != null)
                {
                    //Console.WriteLine("Start Parsing for.limit Variable...");
                    // Performing without an explicit "CLOSE"
                    //LiquidExpression expr = new LiquidExpression();  // create the holding expression
                    StartCapturingVariable(
                        context.for_param_limit().variable(),
                        x => forBlock.Limit = new TreeNode<LiquidExpression>(new LiquidExpression {Expression = x}));


                    //StartNewLiquidExpressionTree(x => forBlock.Limit = x);
                    //StartCapturingVariable(context.for_param_limit().variable());
                    
                    //MarkCurrentExpressionComplete();
                }
                //forBlock.Limit = CreateIntNumericValueFromString(context.for_param_limit().NUMBER().ToString());
            }
            if (context.for_param_offset() != null)
            {
                if (context.for_param_offset().NUMBER() != null)
                {
                    forBlock.Offset = CreateObjectSimpleExpressionNode(
                        CreateIntNumericValueFromString(context.for_param_offset().NUMBER().ToString()));
                }
                else if (context.for_param_offset().variable() != null)
                {
                    StartCapturingVariable(
                        context.for_param_offset().variable(),
                        x => forBlock.Offset = new TreeNode<LiquidExpression>(new LiquidExpression { Expression = x }));
                    //StartNewLiquidExpressionTree(x => forBlock.Offset = x);
                    //StartCapturingVariable(context.for_param_offset().variable());
                    //MarkCurrentExpressionComplete();
                }
            }
        }

        public override void ExitFor_params(LiquidParser.For_paramsContext context)
        {
            base.ExitFor_params(context);
        }

        /// <summary>
        /// Put the iterable (string, array, generator aka range, etc.) into the current block of the "for block" stack.
        /// </summary>
        /// <param name="context"></param>
        public override void EnterFor_iterable(LiquidParser.For_iterableContext context)
        {
            base.EnterFor_iterable(context);
            var forBlock = CurrentBuilderContext.ForBlockStack.Peek();

            // the iterators are going to be created by the visitor
            if (context.STRING() != null)
            {
                forBlock.IterableCreator =
                    new StringValueIterableCreator(GenerateStringSymbol(context.STRING().GetText()));
            }
            else if (context.variable() != null)
            {

//                StartNewLiquidExpressionTree(result =>
//                {
//                    Console.WriteLine("   --- Setting ExpRESSION TREE TO " + result);
//                    forBlock.IterableCreator = new ArrayValueCreator(result);
//                    
//                });
                //StartCapturingVariable(context.variable()); // marked complete in ExitFor_iterable.
                StartCapturingVariable(
                    context.variable(),
                    x => forBlock.IterableCreator = new ArrayValueCreator(new TreeNode<LiquidExpression>(new LiquidExpression { Expression = x })));
                
            }
            else if (context.generator() != null)
            {
                //Console.WriteLine("  +++ FOUND a GENERATOR ");

                CurrentBuilderContext.GeneratorCreator = new GeneratorCreator();
                //forBlock.IterableCreator = CreateGeneratorContext(context.generator());
                forBlock.IterableCreator = CurrentBuilderContext.GeneratorCreator;
            }
            else
            {   
                //Console.WriteLine("TODO: Process the missing iterable");
                // TODO: Maybe put an UNDEFINED variable in the AST?  Or an Erroneous If?
            }
        }

        public override void ExitFor_iterable(LiquidParser.For_iterableContext context)
        {
            base.ExitFor_iterable(context);
            if (context.generator() != null)
            {
                CurrentBuilderContext.GeneratorCreator = null;
            }

        }

        public override void EnterGenerator(LiquidParser.GeneratorContext context)
        {
            base.EnterGenerator(context);
            //Console.WriteLine("/// Entering Generator "+ context.GetText());
            
        }

        public override void ExitGenerator(LiquidParser.GeneratorContext context)
        {
            base.ExitGenerator(context);
            //Console.WriteLine("/// Exiting Generator " + context.GetText());            
        }

        public override void EnterGenerator_index(LiquidParser.Generator_indexContext context)
        {
            base.EnterGenerator_index(context);
            // if this is a number, then add it to the current Generator in 
            // the ContextBuilder.  otherwise, let the Generator 
            //Console.WriteLine("___ ENTER Generator Index " + context.GetText());

            Action<TreeNode<LiquidExpression>> setCurrentExpression;
            if (CurrentBuilderContext.GeneratorCreator.StartExpression == null)
            {
                //Console.WriteLine("==== Assiging START Index " + context.variable().GetText() + " to " + x);
                setCurrentExpression = x =>
                {
                    //Console.WriteLine("SETTING START INDEX to " +x );
                    CurrentBuilderContext.GeneratorCreator.StartExpression = x;
                };
            }
            else
            {
                setCurrentExpression = x =>
                {
                    //Console.WriteLine("SETTING END INDEX to " + x);
                    CurrentBuilderContext.GeneratorCreator.EndExpression = x;
                };

            }

            if (context.NUMBER() != null) // lower range
            {

                setCurrentExpression(CreateObjectSimpleExpressionNode(
                        CreateIntNumericValueFromString(context.NUMBER().GetText())));

            }
            else if (context.variable() != null) // TODO: Move this
            {
                StartCapturingVariable(
                    context.variable(),
                    x => setCurrentExpression(new TreeNode<LiquidExpression>(new LiquidExpression { Expression = x })));

//                StartNewLiquidExpressionTree(x =>
//                {
//                    //Console.WriteLine("==== Assiging index Index " + context.variable().GetText() + " to " + x);
//                    setCurrentExpression(x);
//                });
//                StartCapturingVariable(context.variable());
//                //MB MarkCurrentExpressionComplete();
            }

        }

        public override void ExitGenerator_index(LiquidParser.Generator_indexContext context)
        {
            base.ExitGenerator_index(context); 
            //Console.WriteLine("___ EXIT Generator Index " + context.GetText());

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
 
        #endregion

        #region Cycle Tag

        public override void EnterCycle_tag(LiquidParser.Cycle_tagContext context)
        {
            base.EnterCycle_tag(context);
            var cycleList = new List<TreeNode<LiquidExpression>>();
            Stack<Action> varsToCapture = new Stack<Action>();
            foreach (var obj in context.cycle_value())
            {
                if (obj.BOOLEAN() != null)
                {
                    cycleList.Add(CreateObjectSimpleExpressionNode(new BooleanValue(Convert.ToBoolean(obj.GetText()))));
                }
                if (obj.STRING() != null)
                {
                    cycleList.Add(CreateObjectSimpleExpressionNode(GenerateStringSymbol(obj.STRING().GetText())));
                }
                if (obj.NUMBER() != null)
                {
                    var num = CreateIntNumericValueFromString(obj.NUMBER().ToString());
                    cycleList.Add(CreateObjectSimpleExpressionNode(num));
                }
                if (obj.variable() != null)
                {
                    //Console.WriteLine("Start Parsing CycleTag.cycle_list Variable...");
                    // Performing without an explicit "CLOSE"
                    LiquidExpression expr = new LiquidExpression();  // create the holding expression
                    cycleList.Add(new TreeNode<LiquidExpression>(expr)); // add the (currently empty) expression to the list of vals
                    var obj1 = obj; // avoid weird closure issue
                    varsToCapture.Push(() => // push onto a stack, later to be eval-ed in reverse order.
                        StartCapturingVariable(
                            obj1.variable(),
                            x => expr.Expression = x));
                }
                if (obj.NULL() != null)
                {
                    throw new Exception("Null not implemented yet");
                    //cycleList.Add(CreateObjectSimpleExpressionNode());
                }   
            }
            while (varsToCapture.Count > 0) // eval the queued variable captures.
            {
                var fn = varsToCapture.Pop();
                fn();
            }
            var cycleTag = new CycleTag
            {                     
                CycleList = cycleList
                //CycleList = context.cycle_string().Select(str => (IExpressionConstant) GenerateStringSymbol(str.GetText())).ToList()
            };
            if (context.cycle_group() != null)
            {
                if (context.cycle_group().STRING() != null)
                {
                    cycleTag.GroupNameExpressionTree =
                        CreateObjectSimpleExpressionNode(new StringValue(context.cycle_group().STRING().GetText()));
                }
                if (context.cycle_group().NUMBER() != null)
                {
                    var number = CreateIntNumericValueFromString(context.cycle_group().NUMBER().ToString());
                    cycleTag.GroupNameExpressionTree = CreateObjectSimpleExpressionNode(number);
                }
                if (context.cycle_group().variable() != null)
                {
                    //Console.WriteLine("Start Parsing CycleTag.cycle_group Variable...");
                    StartCapturingVariable(
                        context.cycle_group().variable(),
                            x => cycleTag.GroupNameExpressionTree = new TreeNode<LiquidExpression>(new LiquidExpression { Expression = x }));
                    //StartNewLiquidExpressionTree(x => cycleTag.GroupNameExpressionTree = x);
                    //StartCapturingVariable(context.cycle_group().variable()); // marked complete in ExitCycle_Tag
                    
                }
            }

            CurrentAstNode.AddChild(CreateTreeNode<IASTNode>(cycleTag));
        }


        #endregion

        public override void EnterTablerow_tag(LiquidParser.Tablerow_tagContext context)
        {
            base.EnterTablerow_tag(context);
            var tableRowTag = new TableRowBlockTag
            {
                LocalVariable = context.tablerow_label().VARIABLENAME().ToString()
            };
            var newNode = CreateTreeNode<IASTNode>(tableRowTag);

            CurrentAstNode.AddChild(newNode);
            CurrentBuilderContext.TableRowBlockTagStack.Push(tableRowTag);

            _astNodeStack.Push(tableRowTag.LiquidBlock); // capture the block

        }

        public override void ExitTablerow_tag(LiquidParser.Tablerow_tagContext context)
        {
            base.ExitTablerow_tag(context);
            CurrentBuilderContext.TableRowBlockTagStack.Pop();
            _astNodeStack.Pop();
        }

        public override void EnterTablerow_params(LiquidParser.Tablerow_paramsContext context)
        {
            base.EnterTablerow_params(context);

            var tableRowBlock = CurrentBuilderContext.TableRowBlockTagStack.Peek();
            
            if (context.tablerow_cols() != null)
            {
                if (context.tablerow_cols().NUMBER() != null)
                {
                    tableRowBlock.Cols = CreateObjectSimpleExpressionNode(
                        CreateIntNumericValueFromString(context.tablerow_cols().NUMBER().ToString()));
                }
                else
                {
                    StartCapturingVariable(
                        context.tablerow_cols().variable(),
                        x => tableRowBlock.Cols = new TreeNode<LiquidExpression>(new LiquidExpression { Expression = x }));

                    //StartNewLiquidExpressionTree(x => tableRowBlock.Cols = x);
                    //StartCapturingVariable(context.for_param_limit().variable());
                    //MarkCurrentExpressionComplete();
                }
            }
            if (context.for_param_limit() != null)
            {
                if (context.for_param_limit().NUMBER() != null)
                {
                    tableRowBlock.Limit = CreateObjectSimpleExpressionNode(
                        CreateIntNumericValueFromString(context.for_param_limit().NUMBER().ToString()));
                }
                else
                {
                    StartCapturingVariable(
                        context.for_param_limit().variable(),
                        x => tableRowBlock.Limit  = new TreeNode<LiquidExpression>(new LiquidExpression { Expression = x }));

                    //StartNewLiquidExpressionTree(x => tableRowBlock.Limit = x);
                    //StartCapturingVariable(context.for_param_limit().variable());
                    //MarkCurrentExpressionComplete();
                }
            }
            if (context.for_param_offset() != null)
            {
                if (context.for_param_offset().NUMBER() != null)
                {
                    tableRowBlock.Offset = CreateObjectSimpleExpressionNode(
                        CreateIntNumericValueFromString(context.for_param_offset().NUMBER().ToString()));
                }
                else
                {
                    StartCapturingVariable(
                        context.for_param_offset().variable(),
                        x => tableRowBlock.Offset  = new TreeNode<LiquidExpression>(new LiquidExpression { Expression = x }));
                    //StartNewLiquidExpressionTree(x => tableRowBlock.Offset = x);
                    //StartCapturingVariable(context.for_param_offset().variable());
                    //MarkCurrentExpressionComplete();
                }
            }
            
        }

        public override void EnterTablerow_iterable(LiquidParser.Tablerow_iterableContext context)
        {
            base.EnterTablerow_iterable(context);
            var tableRowBlock = CurrentBuilderContext.TableRowBlockTagStack.Peek();
            if (context.STRING() != null)
            {
                //Console.WriteLine("  +++ FOUND a STRING ");
                tableRowBlock.IterableCreator =
                    new StringValueIterableCreator(GenerateStringSymbol(context.STRING().GetText()));
            }
            else if (context.variable() != null)
            {
                //Console.WriteLine("  +++ FOUND a VARIABLE ");
                StartCapturingVariable(
                    context.variable(),
                    x => tableRowBlock.IterableCreator = new ArrayValueCreator(new TreeNode<LiquidExpression>(new LiquidExpression { Expression = x })));

//                StartNewLiquidExpressionTree(result =>
//                {
//                    //Console.WriteLine("   --- Setting ExpRESSION TREE TO " + result);
//                    tableRowBlock.IterableCreator = new ArrayValueCreator(result);
//
//                });
//                StartCapturingVariable(context.variable()); // marked complete in ExitFor_iterable.


            }
            else if (context.generator() != null)
            {
                //Console.WriteLine("  +++ FOUND a GENERATOR ");
                CurrentBuilderContext.GeneratorCreator = new GeneratorCreator();
                tableRowBlock.IterableCreator = CurrentBuilderContext.GeneratorCreator;
            }
            else
            {
                //Console.WriteLine("TODO: Process the missing iterable");
                // TODO: Maybe put an UNDEFINED variable in the AST?  Or an Erroneous If?
            }

        }

        public override void ExitTablerow_iterable(LiquidParser.Tablerow_iterableContext context)
        {
            base.ExitTablerow_iterable(context);
            if (context.generator() != null)
            {
                CurrentBuilderContext.GeneratorCreator = null;
            }
        }

        #region Custom Tags

        public override void EnterCustom_tag_start(LiquidParser.Custom_tag_startContext context)
        {
            // custom tags are interpreted as CustomTags (not CustomTagBlocks) until we 
            // encounter an end tag.  at that point, the AST is rewritten to reflect this.

            //Console.WriteLine("++ START OF OPENING CUSTOM TAG: " + context.VARIABLENAME());
            base.EnterCustom_tag_start(context);
            var customTag = new CustomTag(context.VARIABLENAME().GetText());
            var newNode = AddNodeToAST(customTag);
            //CurrentBuilderContext.CustomTagStack.Push(customTag);
            //Console.WriteLine("THe PARENT AST node (which needs to be stored in case of reparenting) is " + CurrentAstNode.Data);
            var tuple = new Tuple<CustomTag, TreeNode<IASTNode>>(customTag, newNode);
            _customTagStackAndParent.Push(tuple);
        }

         public override void EnterCustomtag_expr(LiquidParser.Customtag_exprContext context)
        {
            base.EnterCustomtag_expr(context);
            //Console.WriteLine("EXPR IS "+context.outputexpression().GetText());
            
            StartNewLiquidExpressionTree(result =>
            {
                //Console.WriteLine("Setting ExpRESSION TREE TO " + result);
                //CurrentBuilderContext.CustomTagStack.Peek().LiquidExpressionTrees.Add(result);
                _customTagStackAndParent.Peek().Item1.LiquidExpressionTrees.Add(result);
            });
            
        }

//        public override void ExitCustom_tag_start(LiquidParser.Custom_tag_startContext context)
//        {
//            //Console.WriteLine("++ END OF OPENING CUSTOM TAG: " + context.VARIABLENAME());
//            base.ExitCustom_tag_start(context);
//            //_astNodeStack.Pop();
//        }

        public override void EnterCustom_tag_end(LiquidParser.Custom_tag_endContext context)
         {
             //Console.WriteLine("-- START OF CLOSING CUSTOM TAG: " + context.ENDLABEL());
             base.EnterCustom_tag_end(context);
            //CurrentBuilderContext.CustomTagStack.Pop();
         }

        public override void ExitCustom_tag_end(LiquidParser.Custom_tag_endContext context)
        {
            // find the CustomTag that this endtag closes, replace it with a CustomBlockTag,
            // then place all the liquid blocks that were siblings in between the start tag and here
            // into the new CustomBlockTag

           // Console.WriteLine("NEED TO Restructure TREE HERE");
            //Console.WriteLine("-- END OF CLOSING CUSTOM TAG: " + context.ENDLABEL());
            while(true)
            {
                //if (CurrentBuilderContext.CustomTagStack.Count > 0)
                if (_customTagStackAndParent.Count > 0)
                {
                    //var maybeOpeningTag = CurrentBuilderContext.CustomTagStack.Pop();
                    var maybeOpeningTagTuple = _customTagStackAndParent.Pop();
                    var customTagOpen = maybeOpeningTagTuple.Item1;
                    var astForCustomOpeningTag = maybeOpeningTagTuple.Item2;
                    //Console.WriteLine("CHECKING TAG " + customTagOpen.TagName);
                    if (("end" + customTagOpen.TagName).ToLower().Equals(context.ENDLABEL().GetText().ToLower()))
                    {
                        //Console.WriteLine("FOUND OPENING TAG " + customTagOpen.TagName);
                        //Console.WriteLine("TODO: promote all sibling blocks between the start and end so they become the child of " + customTagOpen.TagName);
                        //Console.WriteLine("PARENT IS " + astForCustomOpeningTag);
                        //bool inSibling = false;
                        //var childrenToRemoveFromParent()
                        // find the content block and all its siblings up to here.
                        var childrenToReplace = astForCustomOpeningTag.Parent.Children
                            .SkipWhile(child => child.Data != customTagOpen)
                            .ToList(); // this captures the CustomTag, which needs to be replaced, as well as the children, which need to be attached to the new block.
                        astForCustomOpeningTag.Parent.RemoveChildren(childrenToReplace); // remove these from the parent block
                        var childBlock = new TreeNode<IASTNode>(new RootDocumentNode()); // create a new block
                        childBlock.AddChildren(childrenToReplace.Skip(1)); // and add everything except the customtag
                        IASTNode customTagBlock = CustomBlockTag.CreateFromCustomTag(customTagOpen, childBlock); // recreate customtag as a customtagblock
                        astForCustomOpeningTag.Parent.AddChild(CreateTreeNode(customTagBlock)); // re-add the block to the parent.
                       
                        break;
                    }
                    else
                    {
                        //Console.WriteLine("Not a match");
                    }
                }
                else
                {                  
                    throw new LiquidParserException(new List<LiquidError>  {                       
                        new LiquidError
                        {
                            Line = context.Start.Line,
                            CharPositionInLine = context.Start.Column,
                            Message = "There was no opening tag for the ending tag '" + context.ENDLABEL()+"'"
                        }
                    });

                }
            }

            base.ExitCustom_tag_end(context);

        }

        #endregion

    

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
//            CurrentBuilderContext.IfThenElseBlockStack.Peek().IfElseClauses.Last().GroupNameExpressionTree =
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

        public override void EnterIfchanged_tag(LiquidParser.Ifchanged_tagContext context)
        {
            base.EnterIfchanged_tag(context);
            var ifChangedBlockTag = new IfChangedBlockTag();
            var newNode = CreateTreeNode<IASTNode>(ifChangedBlockTag);
            
            CurrentAstNode.AddChild(newNode);

            _astNodeStack.Push(ifChangedBlockTag.LiquidBlock); // capture the block
        }

        public override void ExitIfchanged_tag(LiquidParser.Ifchanged_tagContext context)
        {
            base.ExitIfchanged_tag(context);
            _astNodeStack.Pop();

        }

        public override void EnterIncrement_tag(LiquidParser.Increment_tagContext incrementContext)
        {
            base.EnterIncrement_tag(incrementContext);
            var incrementTag = new IncrementTag
            {
                VarName = incrementContext.VARIABLENAME().GetText()
            };
           
            var newNode = CreateTreeNode<IASTNode>(incrementTag);
            CurrentAstNode.AddChild(newNode);
        }

        public override void EnterDecrement_tag(LiquidParser.Decrement_tagContext decrementContext)
        {
            base.EnterDecrement_tag(decrementContext);
            var decrementTag = new DecrementTag()
            {
                VarName = decrementContext.VARIABLENAME().GetText()
            };

            var newNode = CreateTreeNode<IASTNode>(decrementTag);
            CurrentAstNode.AddChild(newNode);
        }

        private void StartNewLiquidExpressionTree(Action<TreeNode<LiquidExpression>> setExpression)
        {
            //Console.WriteLine("Set Expression " + setExpression);
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
            //Console.WriteLine(">>>FOUND CASE");
            var caseBlock = new CaseWhenElseBlockTag();

            CurrentBuilderContext.CaseWhenElseBlockStack.Push(caseBlock);
            var newNode = CreateTreeNode<IASTNode>(caseBlock);
            CurrentAstNode.AddChild(newNode);
            
            // TODO: probably the eval-ed value to compare can 
            // be "Equalled" with the when-expression and
            // so converted to a predicate.
            StartNewLiquidExpressionTree(result =>
            {
                //Console.WriteLine("Setting Expression tree to" + result);
                caseBlock.LiquidExpressionTree = result;
            });

        }

        public override void ExitCase_tag(LiquidParser.Case_tagContext context)
        {
            base.ExitCase_tag(context);
            //Console.WriteLine("<<<Exit Case Tag");
            CurrentBuilderContext.CaseWhenElseBlockStack.Pop();
            FinishLiquidExpressionTree();
        }

        public override void EnterCase_tag_contents(LiquidParser.Case_tag_contentsContext context)
        {
            base.EnterCase_tag_contents(context);
            //Console.WriteLine("CASE COntents");
        }

        public override void EnterWhen_tag(LiquidParser.When_tagContext context)
        {
            base.EnterWhen_tag(context);
            //Console.WriteLine("When Tag");
            InitiateWhenClause();

            StartNewLiquidExpressionTree(result =>
            {
                //Console.WriteLine("Setting Expression tree to" + result);
                // TODO: make when clauses multiple.
                //CurrentBuilderContext.CaseWhenElseBlockStack.Peek().WhenClauses.Last().LiquidExpressionTree = result;
                CurrentBuilderContext.CaseWhenElseBlockStack.Peek().WhenClauses.Last().LiquidExpressionTree.Add(result);
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
            //Console.WriteLine("WHEN BLOCK");
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
//                .GroupNameExpressionTree = CreateObjectSimpleExpressionNode(symbol);
        }

        public override void ExitWhen_else_tag(LiquidParser.When_else_tagContext context)
        {
            base.ExitWhen_else_tag(context);
            //Console.WriteLine("Exit When Else");
            //EndWhenClause();
            _astNodeStack.Pop();
        }

        private void InitiateWhenClause()
        {
            var whenBlock = new CaseWhenElseBlockTag.WhenClause();
            //Console.WriteLine("Creating if expressino");
            CurrentBuilderContext.CaseWhenElseBlockStack.Peek().AddWhenBlock(whenBlock);
            _astNodeStack.Push(whenBlock.LiquidBlock); // capture the block
        }
        private void EndWhenClause()
        {
            _astNodeStack.Pop();
        }
        #endregion

        #region Macro Tag

        public override void EnterMacro_tag(LiquidParser.Macro_tagContext macroContext)
        {
            base.EnterMacro_tag(macroContext);
            //Console.WriteLine("Defining MACRO " + macroContext.macro_label().GetText());
            //Console.WriteLine(" --> with parameters " + String.Join(",", macroContext.macro_param().Select(x => x.GetText())));

            var macroBlockTag = new MacroBlockTag(macroContext.macro_label().GetText())
            {
                Args = macroContext.macro_param().Select(x => x.GetText()).ToList()
            };


            AddNodeToAST(macroBlockTag);
            CurrentBuilderContext.MacroBlockTagStack.Push(macroBlockTag);
            _astNodeStack.Push(macroBlockTag.LiquidBlock); // capture the block
        }

        public override void ExitMacro_tag(LiquidParser.Macro_tagContext macroTagContext)
        {
            base.ExitMacro_tag(macroTagContext);
            CurrentBuilderContext.MacroBlockTagStack.Pop();
            _astNodeStack.Pop();
        }

  
        #endregion



        /// <summary>
        /// Mark the current expression complete.  Needs to be called after AddExpressionToCurrentExpressionBuilder.
        /// </summary>
        private void MarkCurrentExpressionComplete()
        {
            //Console.WriteLine("! MarkCurrentExpressionComplete !");
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

        public override void EnterContainsExpression(LiquidParser.ContainsExpressionContext context)
        {
            base.EnterContainsExpression(context);
            //Console.WriteLine(" === creating CONTAINS expression >" + context.GetText() + "<");
            AddExpressionToCurrentExpressionBuilder(new ContainsExpression());
        }

        public override void ExitContainsExpression(LiquidParser.ContainsExpressionContext context)
        {
            base.ExitContainsExpression(context);
            MarkCurrentExpressionComplete();
        }

//        public override void EnterMultExpr(LiquidParser.MultExprContext multContext)
//        {
//            base.EnterMultExpr(multContext);
//            Console.WriteLine(" === creating MULTIPLICATION expression >" + multContext.GetText() + "<");
//        }
//
//        public override void EnterAddSubExpr(LiquidParser.AddSubExprContext addContext)
//        {
//            base.EnterAddSubExpr(addContext);
//            Console.WriteLine(" === creating ADD expression >" + addContext.GetText() + "<");
//        }

        public override void EnterAndExpr(LiquidParser.AndExprContext andContext)
        {
            base.EnterAndExpr(andContext);
            AddExpressionToCurrentExpressionBuilder(new AndExpression());
            //Console.WriteLine(" === creating AND expression >" + andContext.GetText() + "<");
            
        }

        public override void ExitAndExpr(LiquidParser.AndExprContext andContext)
        {
            base.ExitAndExpr(andContext);
            //Console.WriteLine(" --- exiting AND expression >" + andContext.GetText() + "<");
            MarkCurrentExpressionComplete();
        }

        public override void EnterNotExpr(LiquidParser.NotExprContext notContext)
        {
            base.EnterNotExpr(notContext);
            AddExpressionToCurrentExpressionBuilder(new NotExpression());
            //Console.WriteLine(" === creating NOT expression >" + notContext.GetText() + "<");

        }

        public override void ExitNotExpr(LiquidParser.NotExprContext notContext)
        {
            base.ExitNotExpr(notContext);
            //Console.WriteLine(" --- exiting NOT expression >" + notContext.GetText() + "<");
            MarkCurrentExpressionComplete();
        }

        public override void EnterOrExpr(LiquidParser.OrExprContext orContext)
        {
            base.EnterOrExpr(orContext);          
            //Console.WriteLine(" === creating OR expression >" + orContext.GetText() + "<");
            AddExpressionToCurrentExpressionBuilder(new OrExpression());
        }

        public override void ExitOrExpr(LiquidParser.OrExprContext orContext)
        {
            base.ExitOrExpr(orContext);
            //Console.WriteLine(" --- exiting OR expression >" + orContext.GetText() + "<");
            MarkCurrentExpressionComplete();
        }

        public override void EnterIsEmptyOrBlankOrPresentExpr(LiquidParser.IsEmptyOrBlankOrPresentExprContext context)
        {
            base.EnterIsEmptyOrBlankOrPresentExpr(context);
            if (context.NEQ() == null && context.EQ() == null && context.ISEMPTY() == null && context.ISBLANK() != null && context.ISPRESENT() != null)
                // any comparison other than == and != will fail
            {
                AddExpressionToCurrentExpressionBuilder(new FalseExpression());
            }
            else
            {
                if (context.NEQ() != null)
                {
                    //Console.WriteLine("Completing NEQ");
                    AddExpressionToCurrentExpressionBuilder(new NotExpression());
                }

                if (context.EMPTY() != null || context.ISEMPTY() != null)
                {
                    //Console.WriteLine("Completing ISEMPTY");
                    AddExpressionToCurrentExpressionBuilder(new IsEmptyExpression());
                }

                if (context.BLANK() != null || context.ISBLANK() != null)
                {
                    //Console.WriteLine("Completing ISBLANK");
                    AddExpressionToCurrentExpressionBuilder(new IsBlankExpression());
                }

                if (context.PRESENT() != null || context.ISPRESENT() != null)
                {
                    //Console.WriteLine("Completing PRESENT");
                    AddExpressionToCurrentExpressionBuilder(new IsPresentExpression());
                }
            }
        }

        public override void ExitIsEmptyOrBlankOrPresentExpr(LiquidParser.IsEmptyOrBlankOrPresentExprContext context)
        {
            if (context.NEQ() == null 
                && context.EQ() == null 
                && context.ISEMPTY() == null 
                && context.ISBLANK() != null
                && context.ISPRESENT() == null)
            {
                MarkCurrentExpressionComplete();
            }
            else
            {
                if (context.NEQ() != null)
                {
                    MarkCurrentExpressionComplete();
                }

                if (context.EMPTY() != null || context.ISEMPTY() != null)
                {
                    MarkCurrentExpressionComplete();
                }

                if (context.BLANK() != null || context.ISBLANK() != null)
                {
                    MarkCurrentExpressionComplete();
                }

                if (context.PRESENT() != null || context.ISPRESENT() != null)
                {
                    MarkCurrentExpressionComplete();
                }

            }

        }



        public override void EnterComparisonExpr(LiquidParser.ComparisonExprContext comparisonContext)
        {
            base.EnterComparisonExpr(comparisonContext);

            if (comparisonContext.EQ() != null)
            {
                AddExpressionToCurrentExpressionBuilder(new EqualsExpression());
            }
            else if (comparisonContext.GT() != null)
            {
                AddExpressionToCurrentExpressionBuilder(new GreaterThanExpression());
            }
            else if (comparisonContext.LT() != null)
            {
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
            else if (comparisonContext.NEQ() != null)
            {
                AddExpressionToCurrentExpressionBuilder(new NotEqualsExpression());
            }
            else
            {               
                throw new Exception("Invalid comparison: "+ comparisonContext.GetText()); 
            }
        }


        public override void ExitComparisonExpr(LiquidParser.ComparisonExprContext comparisonContext)
        {
            base.ExitComparisonExpr(comparisonContext);
            //Console.WriteLine(" --- exiting COMPARISON expression >" + comparisonContext.GetText() + "<");

            MarkCurrentExpressionComplete();

        }


        // todo: rename this "Object" or something to indicate it's ajust teh Object part of the expression.
        public override void EnterOutputExpression(LiquidParser.OutputExpressionContext context)
        {
            //Console.WriteLine("))) Entering Output Expression! " + context.GetText());
            base.EnterOutputExpression(context);

        }


        public override void ExitOutputExpression(LiquidParser.OutputExpressionContext context)
        {
            base.ExitOutputExpression(context);

            //Console.WriteLine("((( Exiting Output Expression!" + context.GetText() + "<");
            //MarkCurrentExpressionComplete();
        }
        

        /// <summary>
        /// record a new expression
        /// </summary>
        private void AddExpressionToCurrentExpressionBuilder(IExpressionDescription symbol)
        {
            //Console.WriteLine("AddExpressionToCurrentExpressionBuilder "+symbol);
            CurrentBuilderContext.LiquidExpressionBuilder.StartLiquidExpression(symbol);
        }

        private static TreeNode<LiquidExpression> CreateObjectSimpleExpressionNode(IExpressionDescription expressionDescription)
        {
            return new TreeNode<LiquidExpression>(new LiquidExpression { Expression = expressionDescription });
        }



  

        #endregion

        public override void EnterBlock(LiquidParser.BlockContext blockContext)
        {
            //Console.WriteLine(">>> ENTERING BLOCK *" + blockContext.GetText() + "*");
            _blockBuilderContextStack.Push(new BlockBuilderContext());
            base.EnterBlock(blockContext);

        }

        public override void ExitBlock(LiquidParser.BlockContext blockContext)
        {
            //Console.WriteLine(">>> EXITING BLOCK *" + blockContext.GetText() + "*");
            _blockBuilderContextStack.Pop();
            base.ExitBlock(blockContext);

        }

        #region Output / Filter

        public override void EnterObject(LiquidParser.ObjectContext context)
        {
            Console.WriteLine(Indent() + "> ENTER object" + context.GetText());
            base.EnterObject(context);
            var variableReferenceTreeBuilder = new VariableReferenceTreeBuilder();

            CurrentBuilderContext.VarReferenceTreeBuilder.Push(variableReferenceTreeBuilder);
            if (context.variable() != null)
            {
                StartCapturingVariable(
               context.variable(),
               x =>
               {
                   AddExpressionToCurrentExpressionBuilder(x);
                   //MarkCurrentExpressionComplete();
                   //CurrentBuilderContext.VarReferenceTreeBuilder.Peek().InvokeComplete(); // I think this is sort of redundant
               });

            }
            if (context.STRING() != null)
            {
                AddExpressionToCurrentExpressionBuilder(GenerateStringSymbol(context.GetText()));
            }
            if (context.NULL() != null)
            {
                AddExpressionToCurrentExpressionBuilder(null);
            }
            if (context.NULL() != null)
            {
                AddExpressionToCurrentExpressionBuilder(null);
            }
            if (context.NUMBER() != null)
            {
                var liquidExpressionResult = NumericValue.Parse(context.GetText());
                if (liquidExpressionResult.IsError)
                {
                    throw new Exception("Unable to parse " + context.GetText());
                        // this should never occur---the parser only passes valid numeric values.
                }
                AddExpressionToCurrentExpressionBuilder(liquidExpressionResult.SuccessValue<NumericValue>());
            }
        }

        public override void ExitObject(LiquidParser.ObjectContext context)
        {
            Console.WriteLine(Indent() + "< EXIT object" + context.GetText());
            base.ExitObject(context);
            //if (context.variable() != null)
            //{
                CurrentBuilderContext.VarReferenceTreeBuilder.Pop();
            //}
           // else
            //{
                MarkCurrentExpressionComplete();
            //}
        }

        /*
        public override void EnterStringObject(LiquidParser.StringObjectContext context)
        {
            base.EnterStringObject(context);
            //Console.WriteLine("ENTERING SRTRING OBJECT " + context.GetText());
            AddExpressionToCurrentExpressionBuilder(GenerateStringSymbol(context.GetText()));
        }

        public override void ExitStringObject(LiquidParser.StringObjectContext context)
        {
            base.ExitStringObject(context);
            MarkCurrentExpressionComplete();
        }

        /// <summary>
        /// Create a null literal
        /// </summary>
        /// <param name="context"></param>
        public override void EnterNullObject(LiquidParser.NullObjectContext context)
        {
            base.EnterNullObject(context);
            AddExpressionToCurrentExpressionBuilder(null);
        }

        public override void ExitNullObject(LiquidParser.NullObjectContext context)
        {
            base.ExitNullObject(context);
            MarkCurrentExpressionComplete();
        }


        public override void EnterVariableObject(LiquidParser.VariableObjectContext context)
        {
            base.EnterVariableObject(context);
            Console.WriteLine(Indent() + "> ENTER VariableObject " + context.GetText());

            var variableReferenceTreeBuilder = new VariableReferenceTreeBuilder();

            CurrentBuilderContext.VarReferenceTreeBuilder.Push(variableReferenceTreeBuilder);
            StartCapturingVariable(
                context.variable(),
                x =>
                {
                    AddExpressionToCurrentExpressionBuilder(x);
                    MarkCurrentExpressionComplete();
                    //CurrentBuilderContext.VarReferenceTreeBuilder.Peek().InvokeComplete(); // I think this is sort of redundant
                });

            // OK
            //            var variableReferenceTreeBuilder = new VariableReferenceTreeBuilder();
            //            CurrentBuilderContext.VarReferenceTreeBuilder.Push(variableReferenceTreeBuilder);
            //            variableReferenceTreeBuilder.VariableReferenceTreeCompleteEvent +=
            //                x =>
            //                {
            //                    AddExpressionToCurrentExpressionBuilder(x);
            //                    MarkCurrentExpressionComplete();
            //                };
            // END OK
        }

        public override void ExitVariableObject(LiquidParser.VariableObjectContext context)
        {
            Console.WriteLine(Indent() + "< EXIT VariableObject " + context.GetText());
            base.ExitVariableObject(context);
            //CurrentBuilderContext.VarReferenceTreeBuilder.Peek().InvokeComplete();
            CurrentBuilderContext.VarReferenceTreeBuilder.Pop();

        }
        
        public override void EnterNumberObject(LiquidParser.NumberObjectContext context)
        {
            //Console.WriteLine("CREATING NUMBER OBJECT  >" + context.GetText() + "<");
            base.EnterNumberObject(context);
            //ValueCaster.ConvertToM
            var liquidExpressionResult = NumericValue.Parse(context.GetText());
            if (liquidExpressionResult.IsError)
            {
                throw new Exception("Unable to parse " + context.GetText()); // this should never occur---the parser only passes valid numeric values.
            }
            AddExpressionToCurrentExpressionBuilder(liquidExpressionResult.SuccessValue<NumericValue>());

        }

        public override void ExitNumberObject(LiquidParser.NumberObjectContext context)
        {
            base.ExitNumberObject(context);
            MarkCurrentExpressionComplete();
        }


        public override void EnterBooleanObject(LiquidParser.BooleanObjectContext context)
        {
            //Console.WriteLine("CREATING Boolean OBJECT >" + context.GetText() + "<");
            base.EnterBooleanObject(context);
            AddExpressionToCurrentExpressionBuilder(new BooleanValue(Convert.ToBoolean(context.GetText())));
        }

        public override void ExitBooleanObject(LiquidParser.BooleanObjectContext context)
        {
            base.ExitBooleanObject(context);
            MarkCurrentExpressionComplete();
        }
        */


        /// <summary>
        /// TODO: Strip the quotes in the parser/lexer.  Until then, we'll do it here.
        /// </summary>
        private StringValue GenerateStringSymbol(String text)
        {
            return new StringValue(StripQuotes(text));
            //return new StringValue(text);
        }

        private static string StripQuotes(String str)
        {
            return str.Substring(1, str.Length - 2);
        }


        private static NumericValue CreateIntNumericValueFromString(string intstring)
        {
            return NumericValue.Create(Convert.ToInt32(intstring));
        }


        /// <summary>
        /// Enter the {{ ... }} filter
        /// </summary>
        /// <param name="context"></param>
        public override void EnterOutputmarkup(LiquidParser.OutputmarkupContext context)
        {
            //Console.WriteLine("->-ENTERING OUTPUT MARKUP "+context.GetText());
            base.EnterOutputmarkup(context);
            StartNewLiquidExpressionTree(result =>
            {
                //Console.WriteLine("Adding Output Markup Tree Node "+context.GetText());
                //Console.WriteLine(result);                
                CurrentAstNode.AddChild(CreateTreeNode<IASTNode>(new LiquidExpressionTree(result)));

            });


        }

        public override void ExitOutputmarkup(LiquidParser.OutputmarkupContext context)
        {
            //Console.WriteLine("-<-EXITING OUTPUT dMARKUP ");
            base.ExitOutputmarkup(context);
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

            //context.
            //_currentFilterSymbol = new FilterSymbol(context.GetText());
            CurrentBuilderContext.LiquidExpressionBuilder.AddFilterSymbolToLastExpression(new FilterSymbol(context.GetText()));

        }

        
        public override void EnterOutputexpression(LiquidParser.OutputexpressionContext context)
        {
            Console.WriteLine(Indent() + "> ENTER Output Expression");
            base.EnterOutputexpression(context);
            //StartNewLiquidExpressionTree();
            
        }

        public override void ExitOutputexpression(LiquidParser.OutputexpressionContext context)
        {
            //Console.WriteLine("* Exiting Output Expression");
            base.ExitOutputexpression(context);
            Console.WriteLine(Indent() + "< EXIT Output Expression");
        }

        public override void EnterStringFilterArg(LiquidParser.StringFilterArgContext context)
        {
            base.EnterStringFilterArg(context);
            //Console.WriteLine("Enter STRING FILTERARG " + context.GetText());
            CurrentBuilderContext.LiquidExpressionBuilder.AddFilterArgToLastExpressionsFilter(
                CreateObjectSimpleExpressionNode(
                GenerateStringSymbol(context.GetText())));

        }

        public override void EnterNumberFilterArg(LiquidParser.NumberFilterArgContext context)
        {
            base.EnterNumberFilterArg(context);
            //Console.WriteLine("Enter NUMBER FILTERARG " + context.GetText());
            var liquidExpressionResult = NumericValue.Parse(context.GetText());
            if (liquidExpressionResult.IsError)
            {
                throw new Exception("Unable to parse number " + context.GetText()); // this shouldn't occur--the parser should catch it.
            }
            CurrentBuilderContext.LiquidExpressionBuilder.AddFilterArgToLastExpressionsFilter(
                CreateObjectSimpleExpressionNode(liquidExpressionResult.SuccessValue<NumericValue>()));

        }

        public override void EnterBooleanFilterArg(LiquidParser.BooleanFilterArgContext context)
        {
            base.EnterBooleanFilterArg(context);
            //Console.WriteLine("Enter BOOLEAN FILTERARG " + context.GetText());
            CurrentBuilderContext.LiquidExpressionBuilder.AddFilterArgToLastExpressionsFilter(
                CreateObjectSimpleExpressionNode(new BooleanValue(Convert.ToBoolean(context.GetText()))));
        }

        public override void EnterVariableFilterArg(LiquidParser.VariableFilterArgContext context)
        {
            base.EnterVariableFilterArg(context);
            //Console.WriteLine("Enter VARIABLE FILTERARG >" + context.GetText() + "<");

            StartCapturingVariable(
                context.variable(),
                    x => CurrentBuilderContext.LiquidExpressionBuilder.AddFilterArgToLastExpressionsFilter(
                      new TreeNode<LiquidExpression>(new LiquidExpression { Expression = x })));

        }

        public override void ExitVariableFilterArg(LiquidParser.VariableFilterArgContext context)
        {
//            base.ExitVariableFilterArg(context);
//            //CurrentBuilderContext.VarReferenceTreeBuilder.Peek().NotifyListenersOfConstructedVariable();
//            CurrentBuilderContext.VarReferenceTreeBuilder.Peek().EndVariable();
//            CurrentBuilderContext.VarReferenceTreeBuilder.Pop();
        }

        /// <summary>
        /// Save the raw filter argument string.  Liquid says that a filter has
        /// one argument, so this is it.
        /// </summary>
        /// <param name="context"></param>
        public override void EnterFilterargs(LiquidParser.FilterargsContext context)
        {
            base.EnterFilterargs(context);
            //Console.WriteLine("Enter Filter Args "+context.GetText());
            var originalTokens = _tokenStream.GetTokens(context.Start.TokenIndex, context.Stop.TokenIndex);
            // This removes extra whitespace---which may not be what we want...?
            // May need to figure out how to put the whitespace in the hidden stream for this only.
            String normalizedArgs = String.Join(" ", originalTokens.Select(x => x.Text));

            CurrentBuilderContext.LiquidExpressionBuilder.SetRawArgsForLastExpressionsFilter(normalizedArgs);
        }

        #endregion

        public override void EnterFilter(LiquidParser.FilterContext context)
        {
            base.EnterFilter(context);
            //Console.WriteLine("...Entering Filter");
        }

        #region Variables

        /// <summary>
        /// Start capturing the tree of variable references and indices, transforming them as Antlr descends the
        /// tree into a tree of LiquidExpressions.  Each LiquidExpression is a VariableReference + a potential set of filters
        /// to index it.  (The indices may contain nested LiquidExpressions, hence the tree).
        /// </summary>
        private void StartCapturingVariable(
            LiquidParser.VariableContext variableContext, 
            Action<IExpressionDescription> onComplete = null)
        {
            // Create a new variable and put it on the stack.
            Console.WriteLine(Indent() + "===> StartCapturingVariable: START: "+variableContext.GetText());

            var variableReferenceTreeBuilder = new VariableReferenceTreeBuilder();
            CurrentBuilderContext.VarReferenceTreeBuilder.Push(variableReferenceTreeBuilder);
            variableReferenceTreeBuilder.VariableReferenceTreeCompleteEvent +=
                x =>
                {
                    Console.WriteLine(Indent() + "<=== StartCapturingVariable: END: "+variableContext.GetText());
                    //AddExpressionToCurrentExpressionBuilder(x);
                    //MarkCurrentExpressionComplete();
                    if (onComplete != null)
                    {
                       // Console.WriteLine("OoO Calling OnCOmplete");
                        onComplete(x);
                    }
                    CurrentBuilderContext.VarReferenceTreeBuilder.Pop();
                };
        }

        //override Enter

        


        /// <summary>
        /// Create a new variable and add it to the current VariableReferenceStack.
        /// </summary>
        /// <param name="variableContext"></param>
        public override void EnterVariable(LiquidParser.VariableContext variableContext)
        {

            base.EnterVariable(variableContext);
            var varname = variableContext.VARIABLENAME().GetText();
            Console.WriteLine(Indent() + "> ENTER Variable "+ varname);
            //Console.WriteLine("=== ENTER variable " + varname + " ===");
            CurrentBuilderContext.VarReferenceTreeBuilder.Peek().StartVariable();
            CurrentBuilderContext.VarReferenceTreeBuilder.Peek().AddVarName(varname);

        }

        public override void ExitVariable(LiquidParser.VariableContext variableContext)
        {
            base.ExitVariable(variableContext);
            var varname = variableContext.VARIABLENAME().GetText();
            CurrentBuilderContext.VarReferenceTreeBuilder.Peek().EndVariable();
            //CurrentBuilderContext.VarReferenceTreeBuilder.Peek().InvokeComplete();
            Console.WriteLine(Indent() + "< EXIT Variable "+varname);
        }


        public override void EnterObjectvariableindex(LiquidParser.ObjectvariableindexContext context)
        {
            base.EnterObjectvariableindex(context);
            Console.WriteLine(Indent() + "> ENTER Object Variable Index");
            CurrentBuilderContext.VarReferenceTreeBuilder.Peek().StartIndex();
            //var currentVariableReference = CurrentBuilderContext.VariableReferenceStack.Peek();
            if (context.arrayindex() != null)
            {                
                String arrayIndex = context.arrayindex().GetText();
                Console.WriteLine(Indent() + "*** create ARRAY INDEX =>" + arrayIndex);
            }
            if (context.objectproperty() != null)
            {
                String objectProperty = context.objectproperty().GetText();
                Console.WriteLine(Indent() + "*** create OBJECT PROPERTY =>" + objectProperty);
            }
        }

        public override void ExitObjectvariableindex(LiquidParser.ObjectvariableindexContext context)
        {
            Console.WriteLine(Indent() + "< EXIT Object Variable Index");
            base.ExitObjectvariableindex(context);
            CurrentBuilderContext.VarReferenceTreeBuilder.Peek().EndIndex();

        }

        public override void EnterArrayindex(LiquidParser.ArrayindexContext context)
        {
            Console.WriteLine(Indent() + "> ENTER Array Index");
            base.EnterArrayindex(context);
            if (context.ARRAYINT()!= null)
            {
                String arrayIndex = context.ARRAYINT().GetText();
                Console.WriteLine(Indent() + "** ARRAY INT =" + arrayIndex);
                CurrentBuilderContext.VarReferenceTreeBuilder.Peek().StartVariable();
                CurrentBuilderContext.VarReferenceTreeBuilder.Peek().AddIntIndex(Convert.ToInt32(arrayIndex));

            }
            if (context.STRING() != null)
            {
                String arrayIndex = StripQuotes(context.STRING().GetText());
                Console.WriteLine(Indent() + "** ARRAY STRING =" + arrayIndex);
                CurrentBuilderContext.VarReferenceTreeBuilder.Peek().StartVariable();
                CurrentBuilderContext.VarReferenceTreeBuilder.Peek().AddStringIndex(arrayIndex);
            }
            if (context.variable() != null)
            {
                String variable = context.variable().GetText();
                Console.WriteLine(Indent() + "** variable =" + variable);
            }

        }

        public override void ExitArrayindex(LiquidParser.ArrayindexContext context)
        {
            Console.WriteLine(Indent() + "< EXIT Array Index");
            base.ExitArrayindex(context);

        }

        public override void EnterObjectproperty(LiquidParser.ObjectpropertyContext context)
        {
            Console.WriteLine(Indent()+"> Enter Object Property" + context.VARIABLENAME().GetText());
            base.EnterObjectproperty(context);
            // TODO: Make sure syntax like a.b[0] still works.
            if (context.VARIABLENAME() != null)
            {
                String arrayIndex = context.VARIABLENAME().GetText();
                Console.WriteLine(Indent() + "-> ** OBJECT REFERENCE =" + arrayIndex);
                CurrentBuilderContext.VarReferenceTreeBuilder.Peek().StartVariable();
                CurrentBuilderContext.VarReferenceTreeBuilder.Peek().AddStringIndex(context.VARIABLENAME().GetText());
            }       
        }

        #endregion


        private String Indent()
        {
            if (_level < 0)
            {
                return _level.ToString();
            }
            return new String(' ', _level);
        }








        private void ErrorHandler(LiquidError liquiderror)
        {

            //Console.WriteLine("hANDLING ERROR: " + liquiderror);
            //CurrentAstNode.AddChild(CreateTreeNode<IASTNode>(new ErrorNode(liquiderror)));
            this.LiquidErrors.Add(liquiderror);
        }     
       

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

        private TreeNode<IASTNode> AddNodeToAST(IASTNode node)
        {
            var newNode = CreateTreeNode(node);
            CurrentAstNode.AddChild(newNode);
            return newNode;
        }
       
        private class BlockBuilderContext
        {
            //public readonly Stack<CustomTag> CustomTagStack = new Stack<CustomTag>();
            //public readonly Stack<CustomBlockTag> CustomBlockTagStack = new Stack<CustomBlockTag>();
            public readonly Stack<IfThenElseBlockTag> IfThenElseBlockStack = new Stack<IfThenElseBlockTag>();
            public readonly Stack<CaseWhenElseBlockTag> CaseWhenElseBlockStack = new Stack<CaseWhenElseBlockTag>();
            public readonly Stack<MacroBlockTag> MacroBlockTagStack = new Stack<MacroBlockTag>();
            public readonly Stack<ForBlockTag> ForBlockStack = new Stack<ForBlockTag>();
            public readonly Stack<IncludeTag> IncludeTagStack = new Stack<IncludeTag>();
            public readonly Stack<TableRowBlockTag> TableRowBlockTagStack = new Stack<TableRowBlockTag>();  
            public LiquidExpressionTreeBuilder LiquidExpressionBuilder { get; set; }
            public GeneratorCreator GeneratorCreator { get; set; }

            //public readonly Stack<VariableReference> VariableReferenceStack = new Stack<VariableReference>();  
            public readonly Stack<VariableReferenceTreeBuilder> VarReferenceTreeBuilder = new Stack<VariableReferenceTreeBuilder>();

            public IList<String> VerifyStacksEmpty()
            {
                IList<String> errors = new List<String>();
//                if (CustomTagStack.Count > 0)
//                {
//                    errors.Add("CustomTagStack has items");
//                }
//                if (CustomBlockTagStack.Count > 0)
//                {
//                    errors.Add("CustomBlockTagStack has items");
//                }
                if (IfThenElseBlockStack.Count > 0)
                {
                    errors.Add("IfThenElseBlockStack has items");
                }
                if (CaseWhenElseBlockStack.Count > 0)
                {
                    errors.Add("CaseWhenElseBlockStack has items");
                }
                if (MacroBlockTagStack.Count > 0)
                {
                    errors.Add("MacroBlockTagStack has items");
                }
                if (ForBlockStack.Count > 0)
                {
                    errors.Add("ForBlockStack has items");
                }
                if (IncludeTagStack.Count > 0)
                {
                    errors.Add("IncludeTagStack has items");
                }
                if (TableRowBlockTagStack.Count > 0)
                {
                    errors.Add("TableRowBlockTagStack has items");
                }
                if (VarReferenceTreeBuilder.Count > 0)
                {
                    errors.Add("VarReferenceTreeBuilder has items");
                }
                return errors;
            }

        }


        public class VariableReferenceTreeBuilder
        {
            private bool _endOnVariable;
            private int _level = 0;
            private VariableReferenceTree _root;
            private VariableReferenceTree _current;

            public event OnVariableReferenceTreeCompleteEventHandler VariableReferenceTreeCompleteEvent;


            public void StartVariable(bool endOnVariable= true)
            {
                _endOnVariable = endOnVariable;
                _level++;
                Console.WriteLine(Indent() + "# VariableReferenceTreeBuilder.StartVariable()");

                if (_root == null)
                {
                    _current = new VariableReferenceTree();
                    _root = _current;
                }
            }

            public void EndVariable()
            {               
                Console.WriteLine(Indent() + "# VariableReferenceTreeBuilder.EndVariable()");
                //InvokeVariableReferenceTreeCompleteEvent(Result);
                _level--;
            }

            /// <summary>
            /// The indexes at the same level (e.g. a[1][2] are read by liquid.g4 in a line, rather than
            /// as a hierarchy, so this transforms them into a tree.
            /// </summary>
            public void StartIndex()
            {
                _level++;
                Console.WriteLine(Indent() + "# VariableReferenceTreeBuilder.StartIndex()");
                
                if (_current.IndexExpression != null)
                {
                    // insert the new value-index pair above the current node, because
                    // it's on the same level, e.g. a[b][c] <-- inserting c
                    var newParentNode = new VariableReferenceTree();
                    InsertNewNodeAboveCurrent(newParentNode);
                }

                var newNode = new VariableReferenceTree {Parent = _current};

                _current.IndexExpression = newNode;
                _current = newNode;
            }

            private void InsertNewNodeAboveCurrent(VariableReferenceTree newNode)
            {
                Console.WriteLine(Indent() + "Insert new node above current");
                newNode.Value = _current; // current node is now child of newnode

                if (_current.Parent == null) 
                {                    
                    Console.WriteLine(Indent() + "NEW ROOT");
                    _root = newNode; // this is the new toplevel node
                }
                else
                {
                    Console.WriteLine(Indent() + "new node");
                    //IS THIS Value or Index?
                    _current.Parent.IndexExpression = newNode;
                    //_current.Parent.Value = newNode; // The old parent must point to the new node
                }

                newNode.Parent = _current.Parent;
                _current.Parent = newNode;
                _current = newNode;
                //EndIndex();
            }

            public void EndIndex()
            {
               
                Console.WriteLine(Indent() + "# VariableReferenceTreeBuilder.EndIndex()");
                if (_current == null)
                {
                    Console.WriteLine(Indent() + "Current is null");
                }
                else
                {
                    _current = _current.Parent;
                }
                _level--;
            }

            public void AddVarName(String varname)
            {
                Console.WriteLine(Indent() + "# VariableReferenceTreeBuilder.AddVarName("+varname+")");
                _current.Value = new VariableReference(varname);
            }

            public void AddIntIndex(int index)
            {
                Console.WriteLine(Indent() + "# VariableReferenceTreeBuilder.AddIntIndex(" + index+ ")");
                _current.Value = NumericValue.Create(index);
            }

            public void AddStringIndex(String index)
            {
                Console.WriteLine(Indent() + "# VariableReferenceTreeBuilder.AddStringIndex(" + index + ")");
                _current.Value = new StringValue(index);

            }

            private String Indent()
            {
                if (_level < 0)
                {
                    return _level.ToString();
                }
                return new String(' ', _level);
            }
            public VariableReferenceTree Result
            {
                get { return _root; }
            }

            private void InvokeVariableReferenceTreeCompleteEvent(VariableReferenceTree variableReferenceTree)
            {
                OnVariableReferenceTreeCompleteEventHandler handler = VariableReferenceTreeCompleteEvent;
                if (handler != null)
                {
                    Console.WriteLine(Indent() + "===> InvokeVariableReferenceTreeCompleteEvent: Notifying variable complete");
                    handler(variableReferenceTree);
                }
                else
                {
                    Console.WriteLine(Indent() + "*** WARNING: No one to notify about variable.");
                }
            }

            public void InvokeComplete()
            {
                Console.WriteLine("LEVEL IS " + _level);
                //if (isEndVariable && _endOnVariable)
                //{
                //if (_level == 0)
                //{
                    InvokeVariableReferenceTreeCompleteEvent(Result);
                //}
                //}
            }
        }
    }

    public delegate void OnVariableReferenceTreeCompleteEventHandler(VariableReferenceTree args);

}
